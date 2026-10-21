using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Sepiabox.Ingest.Scanning;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Storage;

namespace Sepiabox.Ingest.Uploading;

/// <summary>This computer's clock is too far from the storage server's for face times to be trusted (S-022, D-081).</summary>
public sealed class ClockSkewException(TimeSpan skew) : Exception(Describe(skew))
{
    public TimeSpan Skew { get; } = skew;

    private static string Describe(TimeSpan skew) =>
        $"This computer's clock is {Math.Abs(skew.TotalMinutes):N0} minutes {(skew > TimeSpan.Zero ? "ahead of" : "behind")} the storage server's. " +
        $"Set it right (synchronize it with internet time; on Windows: Settings → Time & language → Date & time → Sync now) " +
        $"and upload again: faces are kept or erased by the time they were found, " +
        $"so upload refuses a clock more than {UploadCommand.MaxClockSkew.TotalMinutes:N0} minutes off.";
}

public sealed record UploadResult(
    int Assets,
    int Uploaded,
    int Skipped,
    long Bytes,
    TimeSpan Duration,
    IReadOnlyList<ScanProblem> Problems)
{
    /// <summary>Assets uploaded with something left out, such as faces stamped by a clock that was ahead (D-081).</summary>
    public IReadOnlyList<ScanProblem> Warnings { get; init; } = [];
}

/// <summary>
/// Sends a <c>process</c> work folder to Blob Storage (D-004, D-005): each asset's original, then its
/// derivatives, then its manifest, because the manifest's arrival is what triggers the cloud import.
/// </summary>
public static class UploadCommand
{
    public const string ConnectionVariable = "SEPIABOX_BLOB_CONNECTION";

    public const int BlockSize = 8 * 1024 * 1024;
    public const int ParallelUploads = 4;

    /// <summary>
    /// How far this computer's clock may be from the storage server's (S-022, D-081): <c>facesDetectedAt</c> comes from
    /// it and is compared with the server's erase and switch-on times.
    /// </summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public static BlobServiceClient CreateClient(UploadOptions options) => options.Destination switch
    {
        UploadDestination.Azure => new BlobServiceClient(
            new Uri($"https://{options.Account}.blob.core.windows.net"), new DefaultAzureCredential()),
        _ => new BlobServiceClient(Environment.GetEnvironmentVariable(ConnectionVariable) ?? "UseDevelopmentStorage=true"),
    };

    public static Task<UploadResult> RunAsync(
        UploadOptions options,
        BlobServiceClient service,
        IProgress<long>? bytesSent = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(options, service, bytesSent, new ManifestValidator(), cancellationToken);

    /// <summary>Takes the validator as a parameter so tests can reach the checks that the schema runs first.</summary>
    internal static async Task<UploadResult> RunAsync(
        UploadOptions options,
        BlobServiceClient service,
        IProgress<long>? bytesSent,
        IManifestValidator validator,
        CancellationToken cancellationToken,
        TimeProvider? clock = null)
    {
        var workFolder = Path.GetFullPath(options.WorkFolder);
        var manifestFolder = Path.Combine(workFolder, "manifests");
        var databasePath = Path.Combine(workFolder, "state.db");
        if (!Directory.Exists(manifestFolder) || !File.Exists(databasePath))
        {
            throw new DirectoryNotFoundException($"{workFolder} is not a work folder written by process.");
        }

        var containers = new Containers(
            service.GetBlobContainerClient(BlobLayout.OriginalsContainer),
            service.GetBlobContainerClient(BlobLayout.DerivativesContainer),
            service.GetBlobContainerClient(BlobLayout.ManifestsContainer));
        foreach (var container in new[] { containers.Originals, containers.Derivatives, containers.Manifests })
        {
            await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        }

        await CheckClockAsync(containers.Manifests, clock ?? TimeProvider.System, cancellationToken);

        using var state = new UploadState(databasePath);
        var run = new Run(
            workFolder,
            containers,
            state,
            // Azurite does not know the Cold tier, so it is only set on real accounts.
            options.Destination == UploadDestination.Azure ? AccessTier.Cold : (AccessTier?)null,
            validator,
            new ManifestStore(validator),
            bytesSent,
            clock ?? TimeProvider.System);

        var stopwatch = Stopwatch.StartNew();
        var manifestFiles = Directory.GetFiles(manifestFolder, "*.json");
        await Parallel.ForEachAsync(
            manifestFiles,
            new ParallelOptions { MaxDegreeOfParallelism = ParallelUploads, CancellationToken = cancellationToken },
            run.UploadAssetAsync);

        return new UploadResult(
            manifestFiles.Length, run.Uploaded, run.Skipped, run.Bytes, stopwatch.Elapsed, [.. run.Problems.OrderBy(p => p.Path)])
        {
            Warnings = [.. run.Warnings.OrderBy(p => p.Path)],
        };
    }

    /// <summary>
    /// Refuses to upload when this computer's clock is more than <see cref="MaxClockSkew"/> from the storage server's,
    /// read from the <c>Date</c> header of a request to it.
    /// </summary>
    internal static async Task CheckClockAsync(BlobContainerClient container, TimeProvider clock, CancellationToken cancellationToken)
    {
        var before = clock.GetUtcNow();
        var response = (await container.GetPropertiesAsync(cancellationToken: cancellationToken)).GetRawResponse();
        var after = clock.GetUtcNow();
        if (response.Headers.Date is not { } server)
        {
            return;
        }

        // The server wrote the header somewhere between our before and after, in whole seconds (rounded down): the clocks
        // differ only when it falls more than MaxClockSkew outside that window, however slow the request was.
        if (server < before - MaxClockSkew - TimeSpan.FromSeconds(1) || server > after + MaxClockSkew)
        {
            throw new ClockSkewException(before + ((after - before) / 2) - server);
        }
    }

    /// <summary>
    /// Where a manifest's derivative is on disk, if it is a file inside <c>derivatives/&lt;id&gt;/</c> of the
    /// work folder. Compared with GetRelativePath rather than a string prefix, so a work folder that is a
    /// drive or filesystem root (F:\ or /) works too. The id must be one plain name, or it would move the
    /// asset folder itself out of <c>derivatives/</c>.
    /// </summary>
    internal static bool TryResolveDerivative(string workFolder, string assetId, string relativePath, out string fullPath)
    {
        fullPath = "";
        if (assetId is "" or "." or ".." || assetId.IndexOfAny(['/', '\\']) >= 0 || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        try
        {
            var derivativesFolder = Path.GetFullPath(Path.Combine(workFolder, "derivatives"));
            var assetFolder = Path.GetFullPath(Path.Combine(derivativesFolder, assetId));
            fullPath = Path.GetFullPath(Path.Combine(workFolder, relativePath));
            return IsStrictlyInside(derivativesFolder, assetFolder)
                && IsStrictlyInside(assetFolder, fullPath)
                && relativePath.IndexOf('\0') < 0;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            fullPath = "";
            return false;
        }
    }

    private static bool IsStrictlyInside(string folder, string path)
    {
        var inside = Path.GetRelativePath(folder, path);
        return !Path.IsPathRooted(inside)
            && inside != "."
            && inside != ".."
            && !inside.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private sealed record Containers(BlobContainerClient Originals, BlobContainerClient Derivatives, BlobContainerClient Manifests);

    private sealed class Run(
        string workFolder,
        Containers containers,
        UploadState state,
        AccessTier? originalTier,
        IManifestValidator validator,
        ManifestStore manifestStore,
        IProgress<long>? bytesSent,
        TimeProvider clock)
    {
        private int _uploaded;
        private int _skipped;
        private long _bytes;

        public int Uploaded => _uploaded;
        public int Skipped => _skipped;
        public long Bytes => _bytes;
        public List<ScanProblem> Problems { get; } = [];
        public List<ScanProblem> Warnings { get; } = [];

        public async ValueTask UploadAssetAsync(string manifestPath, CancellationToken cancellationToken)
        {
            try
            {
                // Taken before reading: if process rewrites the manifest meanwhile, the next run sees a new
                // fingerprint and sends it again instead of recording the old content as uploaded.
                var manifestFile = new FileInfo(manifestPath);
                var fingerprint = $"{manifestFile.Length}:{manifestFile.LastWriteTimeUtc.Ticks}";
                // Checked before anything of this asset is sent: a hand-edited or truncated manifest must
                // not put an original or a derivative in storage that no valid manifest describes.
                if (!validator.TryValidate(await File.ReadAllTextAsync(manifestPath, cancellationToken), out var manifest, out var invalid))
                {
                    AddProblem(manifestPath, $"this manifest is not valid: {invalid}");
                    return;
                }

                var derivativeFiles = new List<(ManifestDerivative Derivative, string Path)>();
                foreach (var derivative in manifest.Derivatives)
                {
                    // The schema already only allows derivatives/<id>/<file>; this keeps that promise even if it changes.
                    if (!TryResolveDerivative(workFolder, manifest.Id, derivative.Path, out var file))
                    {
                        AddProblem(manifestPath, $"derivative {derivative.Name} is outside derivatives/{manifest.Id}/: {derivative.Path}");
                        return;
                    }

                    derivativeFiles.Add((derivative, file));
                }

                // Order matters: an asset's manifest is only sent once everything it points to is in storage.
                if (!await UploadOriginalAsync(manifest, cancellationToken))
                {
                    return;
                }

                foreach (var (derivative, file) in derivativeFiles)
                {
                    var blobName = BlobLayout.DerivativeBlobName(manifest.Id, derivative);
                    await UploadWorkFileAsync(containers.Derivatives, blobName, file, derivative.ContentType, cancellationToken);
                }

                await UploadManifestAsync(manifest, manifestPath, fingerprint, cancellationToken);
            }
            catch (Exception ex) when (IsAssetFailure(ex))
            {
                // A cancel can surface as the I/O or storage error it caused; the user still asked to stop.
                cancellationToken.ThrowIfCancellationRequested();
                AddProblem(manifestPath, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// What one asset can fail with without saying anything about the others: files, permissions,
        /// storage, JSON, an operation cancelled that the user did not cancel (an Azure SDK timeout), and the
        /// AggregateException Azure.Core's RetryPolicy throws when every try failed with one of those.
        /// Anything else is a bug and stops the run.
        /// </summary>
        private static bool IsAssetFailure(Exception ex) => ex switch
        {
            AggregateException aggregate => aggregate.Flatten().InnerExceptions is { Count: > 0 } inner && inner.All(IsAssetFailure),
            _ => ex is IOException or UnauthorizedAccessException or RequestFailedException or System.Text.Json.JsonException
                or InvalidDataException or OperationCanceledException,
        };

        private async Task<bool> UploadOriginalAsync(Manifest manifest, CancellationToken cancellationToken)
        {
            var blob = containers.Originals.GetBlockBlobClient(manifest.Id);
            if (state.IsUploaded(BlobLayout.OriginalsContainer, manifest.Id, manifest.Id))
            {
                Interlocked.Increment(ref _skipped);
                return true;
            }

            if (await ExistingSizeAsync(blob, cancellationToken) == manifest.SizeBytes)
            {
                state.MarkUploaded(BlobLayout.OriginalsContainer, manifest.Id, manifest.Id);
                Interlocked.Increment(ref _skipped);
                return true;
            }

            var source = state.SourcePaths(manifest.Id).FirstOrDefault(File.Exists);
            if (source is null)
            {
                AddProblem(manifest.Id, "Original not found. Connect the drive it was processed from and run upload again.");
                return false;
            }

            if (new FileInfo(source).Length != manifest.SizeBytes || !await UploadVerifiedAsync(blob, source, manifest, cancellationToken))
            {
                AddProblem(source, "Changed since it was processed; not uploaded. Run process again first.");
                return false;
            }

            state.MarkUploaded(BlobLayout.OriginalsContainer, manifest.Id, manifest.Id);
            Interlocked.Increment(ref _uploaded);
            return true;
        }

        // The original is read once: hashed while its blocks are staged, and committed only if it still
        // has the content the manifest describes. On a mismatch the staged blocks are simply abandoned.
        private async Task<bool> UploadVerifiedAsync(BlockBlobClient blob, string path, Manifest manifest, CancellationToken cancellationToken)
        {
            await using var stream = MediaFile.OpenRead(path);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(BlockSize);
            var blockIds = new List<string>();
            try
            {
                int read;
                while ((read = await stream.ReadAtLeastAsync(buffer.AsMemory(0, BlockSize), BlockSize, throwOnEndOfStream: false, cancellationToken)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    // Block IDs must all have the same length within a blob.
                    var blockId = Convert.ToBase64String(Encoding.ASCII.GetBytes(blockIds.Count.ToString("D8", System.Globalization.CultureInfo.InvariantCulture)));
                    using var block = new MemoryStream(buffer, 0, read, writable: false);
                    await blob.StageBlockAsync(blockId, block, cancellationToken: cancellationToken);
                    blockIds.Add(blockId);
                    AddBytes(read);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (Convert.ToHexStringLower(hash.GetHashAndReset()) != manifest.Id)
            {
                return false;
            }

            await blob.CommitBlockListAsync(
                blockIds,
                new CommitBlockListOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = manifest.ContentType },
                    AccessTier = originalTier,
                },
                cancellationToken);
            return true;
        }

        private async Task UploadWorkFileAsync(
            BlobContainerClient container, string blobName, string path, string contentType, CancellationToken cancellationToken)
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                throw new FileNotFoundException($"{path} is listed in the manifest but missing. Run process again.", path);
            }

            // Work-folder files are rewritten in place by process, so size and write time tell when one changed.
            var fingerprint = $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
            if (state.IsUploaded(container.Name, blobName, fingerprint))
            {
                Interlocked.Increment(ref _skipped);
                return;
            }

            var blob = container.GetBlobClient(blobName);
            if (await ExistingSizeAsync(blob, cancellationToken) == file.Length)
            {
                state.MarkUploaded(container.Name, blobName, fingerprint);
                Interlocked.Increment(ref _skipped);
                return;
            }

            long reported = 0;
            await blob.UploadAsync(
                path,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                    TransferOptions = new StorageTransferOptions
                    {
                        InitialTransferSize = BlockSize,
                        MaximumTransferSize = BlockSize,
                        MaximumConcurrency = 1,
                    },
                    ProgressHandler = new Progress(total =>
                    {
                        AddBytes(total - reported);
                        reported = total;
                    }),
                },
                cancellationToken);

            state.MarkUploaded(container.Name, blobName, fingerprint);
            Interlocked.Increment(ref _uploaded);
        }

        /// <summary>
        /// Merges this work folder's manifest into the one already in storage (D-036): another drive may
        /// have uploaded the same asset with its own sources. A manifest that cannot be merged is a
        /// problem for this asset only, and is tried again on the next run.
        /// </summary>
        private async Task UploadManifestAsync(Manifest manifest, string path, string fingerprint, CancellationToken cancellationToken)
        {
            var blobName = BlobLayout.ManifestBlobName(manifest.Id);
            if (state.IsUploaded(BlobLayout.ManifestsContainer, blobName, fingerprint))
            {
                Interlocked.Increment(ref _skipped);
                return;
            }

            // The clock was checked at the start: faces "found" later than now were stamped by a clock that was ahead when
            // process ran, so their time cannot be compared with an erase (D-081).
            if (manifest.FacesDetectedAt is { } detected && detected > clock.GetUtcNow() + ManifestMerge.DetectionTolerance)
            {
                manifest = ManifestMerge.WithoutFaces(manifest);
                lock (Warnings)
                {
                    Warnings.Add(new ScanProblem(path, $"its faces say they were found at {detected:u}, after now: this computer's clock was ahead when process ran, so they were left out (run process --faces into a fresh work folder)"));
                }
            }

            var result = await manifestStore.WriteMergedAsync(containers.Manifests, manifest, cancellationToken);
            switch (result.Status)
            {
                case ManifestWriteStatus.Problem:
                    AddProblem(path, result.Problem!);
                    return;
                case ManifestWriteStatus.Unchanged:
                    Interlocked.Increment(ref _skipped);
                    break;
                default:
                    AddBytes(result.Bytes);
                    Interlocked.Increment(ref _uploaded);
                    break;
            }

            state.MarkUploaded(BlobLayout.ManifestsContainer, blobName, fingerprint);
        }

        private static async Task<long?> ExistingSizeAsync(BlobBaseClient blob, CancellationToken cancellationToken)
        {
            try
            {
                var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
                return properties.Value.ContentLength;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        private void AddBytes(long count) => bytesSent?.Report(Interlocked.Add(ref _bytes, count));

        private void AddProblem(string path, string reason)
        {
            lock (Problems)
            {
                Problems.Add(new ScanProblem(path, reason));
            }
        }
    }

    // Reports synchronously, unlike System.Progress<T>, so the byte count is never read out of order.
    private sealed class Progress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }
}
