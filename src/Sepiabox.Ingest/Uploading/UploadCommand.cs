using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using Sepiabox.Ingest.Processing;
using Sepiabox.Ingest.Scanning;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Storage;

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

    /// <summary>Whether the work folder is index-only (S-063): its originals are never uploaded.</summary>
    public bool IndexOnly { get; init; }

    /// <summary>Assets whose upload records were forgotten by <c>--resend-source</c>, so they were sent again (S-057).</summary>
    public int AssetsResent { get; init; }

    /// <summary>Assets whose original was left at its source, in an index-only work folder; the original is never opened.</summary>
    public int OriginalsLeftAtSource { get; init; }
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

    public static Task<UploadResult> RunAsync(
        UploadOptions options,
        IBlobStore store,
        IProgress<long>? bytesSent = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(options, store, bytesSent, new ManifestValidator(), cancellationToken);

    /// <summary>Takes the validator as a parameter so tests can reach the checks that the schema runs first.</summary>
    internal static async Task<UploadResult> RunAsync(
        UploadOptions options,
        IBlobStore store,
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

        foreach (var container in new[] { BlobLayout.OriginalsContainer, BlobLayout.DerivativesContainer, BlobLayout.ManifestsContainer })
        {
            await store.CreateContainerAsync(container, cancellationToken);
        }

        await CheckClockAsync(store, BlobLayout.ManifestsContainer, clock ?? TimeProvider.System, cancellationToken);

        using var state = new UploadState(databasePath);
        var run = new Run(
            workFolder,
            store,
            state,
            // Azurite does not know the Cold tier, so it is only asked for on real accounts.
            options.Destination == UploadDestination.Azure ? BlobStorageClass.Cold : BlobStorageClass.Default,
            validator,
            new ManifestStore(validator, store),
            bytesSent,
            clock ?? TimeProvider.System,
            state.Mode == WorkFolderMode.IndexOnly);

        var stopwatch = Stopwatch.StartNew();
        var manifestFiles = Directory.GetFiles(manifestFolder, "*.json");
        var resent = options.ResendSource is { } resendSource ? ForgetSource(state, manifestFiles, resendSource, validator) : 0;
        await Parallel.ForEachAsync(
            manifestFiles,
            new ParallelOptions { MaxDegreeOfParallelism = ParallelUploads, CancellationToken = cancellationToken },
            run.UploadAssetAsync);

        return new UploadResult(
            manifestFiles.Length, run.Uploaded, run.Skipped, run.Bytes, stopwatch.Elapsed, [.. run.Problems.OrderBy(p => p.Path)])
        {
            Warnings = [.. run.Warnings.OrderBy(p => p.Path)],
            IndexOnly = run.IndexOnly,
            OriginalsLeftAtSource = run.LeftAtSource,
            AssetsResent = resent,
        };
    }

    // The assets of a source whose records are forgotten, so they are sent again (S-057); a manifest that cannot be read
    // is left to the upload itself to report.
    private static int ForgetSource(UploadState state, IEnumerable<string> manifestFiles, string source, IManifestValidator validator)
    {
        var assets = 0;
        foreach (var file in manifestFiles)
        {
            if (validator.TryValidate(File.ReadAllText(file), out var manifest, out _) && manifest.Sources.Any(s => s.Name == source))
            {
                state.Forget(manifest.Id);
                assets++;
            }
        }

        return assets;
    }

    /// <summary>
    /// Refuses to upload when this computer's clock is more than <see cref="MaxClockSkew"/> from the storage server's,
    /// as it answers a request to it (<see cref="IBlobStore.GetServerTimeAsync"/>).
    /// </summary>
    internal static async Task CheckClockAsync(IBlobStore store, string container, TimeProvider clock, CancellationToken cancellationToken)
    {
        var before = clock.GetUtcNow();
        var serverTime = await store.GetServerTimeAsync(container, cancellationToken);
        var after = clock.GetUtcNow();
        if (serverTime is not { } server)
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

    private sealed class Run(
        string workFolder,
        IBlobStore store,
        UploadState state,
        BlobStorageClass originalClass,
        IManifestValidator validator,
        ManifestStore manifestStore,
        IProgress<long>? bytesSent,
        TimeProvider clock,
        bool indexOnly)
    {
        private int _uploaded;
        private int _leftAtSource;
        private int _skipped;
        private long _bytes;

        public bool IndexOnly => indexOnly;
        public int LeftAtSource => _leftAtSource;
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
                if (indexOnly)
                {
                    // The original stays at its source (S-063, D-088): it is never opened, and the drive or export
                    // need not be connected.
                    Interlocked.Increment(ref _leftAtSource);
                }
                else if (!await UploadOriginalAsync(manifest, cancellationToken))
                {
                    return;
                }

                foreach (var (derivative, file) in derivativeFiles)
                {
                    var blobName = BlobLayout.DerivativeBlobName(manifest.Id, derivative.Path);
                    await UploadWorkFileAsync(BlobLayout.DerivativesContainer, blobName, file, derivative.ContentType, cancellationToken);
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
        /// storage (<see cref="IBlobStore.IsStoreFailure"/>), JSON, an operation cancelled that the user did not cancel
        /// (a storage SDK timeout), and the AggregateException an SDK's retry policy throws when every try failed with
        /// one of those. Anything else is a bug and stops the run.
        /// </summary>
        private bool IsAssetFailure(Exception ex) => ex switch
        {
            AggregateException aggregate => aggregate.Flatten().InnerExceptions is { Count: > 0 } inner && inner.All(IsAssetFailure),
            _ => ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                or InvalidDataException or OperationCanceledException || store.IsStoreFailure(ex),
        };

        private async Task<bool> UploadOriginalAsync(Manifest manifest, CancellationToken cancellationToken)
        {
            if (state.IsUploaded(BlobLayout.OriginalsContainer, manifest.Id, manifest.Id))
            {
                Interlocked.Increment(ref _skipped);
                return true;
            }

            if (await store.GetSizeAsync(BlobLayout.OriginalsContainer, manifest.Id, cancellationToken) == manifest.SizeBytes)
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

            if (new FileInfo(source).Length != manifest.SizeBytes || !await StageFileAsync(BlobLayout.OriginalsContainer, manifest.Id, source, manifest.ContentType, originalClass, manifest.Id, cancellationToken))
            {
                AddProblem(source, "Changed since it was processed; not uploaded. Run process again first.");
                return false;
            }

            state.MarkUploaded(BlobLayout.OriginalsContainer, manifest.Id, manifest.Id);
            Interlocked.Increment(ref _uploaded);
            return true;
        }

        // The file is read once: hashed while its blocks are staged, and committed only if it has the content the
        // expected hash describes (none: always). On a mismatch the upload is disposed uncommitted and leaves no blob (D-030).
        private async Task<bool> StageFileAsync(
            string container, string blobName, string path, string contentType, BlobStorageClass storageClass, string? expectedHash,
            CancellationToken cancellationToken)
        {
            await using var stream = MediaFile.OpenRead(path);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(BlockSize);
            IStagedUpload? upload = null;
            try
            {
                var read = await stream.ReadAtLeastAsync(buffer.AsMemory(0, BlockSize), BlockSize, throwOnEndOfStream: false, cancellationToken);
                if (expectedHash is null && read < BlockSize)
                {
                    // A file of one block or less (a thumbnail, a preview) is one request, not a block and a commit: with
                    // tens of thousands of photos the difference is hours (D-100). Derivatives keep the default class.
                    await store.WriteAsync(
                        container, blobName, BinaryData.FromBytes(buffer.AsMemory(0, read)), contentType, BlobWriteCondition.Always, cancellationToken);
                    AddBytes(read);
                    return true;
                }

                upload = store.BeginUpload(container, blobName);
                while (read > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await upload.AppendAsync(buffer.AsMemory(0, read), cancellationToken);
                    AddBytes(read);
                    read = await stream.ReadAtLeastAsync(buffer.AsMemory(0, BlockSize), BlockSize, throwOnEndOfStream: false, cancellationToken);
                }

                if (expectedHash is not null && Convert.ToHexStringLower(hash.GetHashAndReset()) != expectedHash)
                {
                    return false;
                }

                await upload.CommitAsync(contentType, storageClass, cancellationToken);
                return true;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                if (upload is not null)
                {
                    await upload.DisposeAsync();
                }
            }
        }

        private async Task UploadWorkFileAsync(
            string container, string blobName, string path, string contentType, CancellationToken cancellationToken)
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                throw new FileNotFoundException($"{path} is listed in the manifest but missing. Run process again.", path);
            }

            // Work-folder files are rewritten in place by process, so size and write time tell when one changed.
            var fingerprint = $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
            if (state.IsUploaded(container, blobName, fingerprint))
            {
                Interlocked.Increment(ref _skipped);
                return;
            }

            if (await store.GetSizeAsync(container, blobName, cancellationToken) == file.Length)
            {
                state.MarkUploaded(container, blobName, fingerprint);
                Interlocked.Increment(ref _skipped);
                return;
            }

            await StageFileAsync(container, blobName, path, contentType, BlobStorageClass.Default, expectedHash: null, cancellationToken);

            state.MarkUploaded(container, blobName, fingerprint);
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

            var result = await manifestStore.WriteMergedAsync(BlobLayout.ManifestsContainer, manifest, cancellationToken);
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

        private void AddBytes(long count) => bytesSent?.Report(Interlocked.Add(ref _bytes, count));

        private void AddProblem(string path, string reason)
        {
            lock (Problems)
            {
                Problems.Add(new ScanProblem(path, reason));
            }
        }
    }
}
