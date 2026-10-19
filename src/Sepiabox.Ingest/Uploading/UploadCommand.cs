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

public sealed record UploadResult(
    int Assets,
    int Uploaded,
    int Skipped,
    long Bytes,
    TimeSpan Duration,
    IReadOnlyList<ScanProblem> Problems);

/// <summary>
/// Sends a <c>process</c> work folder to Blob Storage (D-004, D-005): each asset's original, then its
/// derivatives, then its manifest, because the manifest's arrival is what triggers the cloud import.
/// </summary>
public static class UploadCommand
{
    public const string ConnectionVariable = "SEPIABOX_BLOB_CONNECTION";

    public const int BlockSize = 8 * 1024 * 1024;
    public const int ParallelUploads = 4;

    public static BlobServiceClient CreateClient(UploadOptions options) => options.Destination switch
    {
        UploadDestination.Azure => new BlobServiceClient(
            new Uri($"https://{options.Account}.blob.core.windows.net"), new DefaultAzureCredential()),
        _ => new BlobServiceClient(Environment.GetEnvironmentVariable(ConnectionVariable) ?? "UseDevelopmentStorage=true"),
    };

    public static async Task<UploadResult> RunAsync(
        UploadOptions options,
        BlobServiceClient service,
        IProgress<long>? bytesSent = null,
        CancellationToken cancellationToken = default)
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

        using var state = new UploadState(databasePath);
        var run = new Run(
            workFolder,
            containers,
            state,
            // Azurite does not know the Cold tier, so it is only set on real accounts.
            options.Destination == UploadDestination.Azure ? AccessTier.Cold : (AccessTier?)null,
            new ManifestStore(new ManifestValidator()),
            bytesSent);

        var stopwatch = Stopwatch.StartNew();
        var manifestFiles = Directory.GetFiles(manifestFolder, "*.json");
        await Parallel.ForEachAsync(
            manifestFiles,
            new ParallelOptions { MaxDegreeOfParallelism = ParallelUploads, CancellationToken = cancellationToken },
            run.UploadAssetAsync);

        return new UploadResult(
            manifestFiles.Length, run.Uploaded, run.Skipped, run.Bytes, stopwatch.Elapsed, [.. run.Problems.OrderBy(p => p.Path)]);
    }

    private sealed record Containers(BlobContainerClient Originals, BlobContainerClient Derivatives, BlobContainerClient Manifests);

    private sealed class Run(
        string workFolder,
        Containers containers,
        UploadState state,
        AccessTier? originalTier,
        ManifestStore manifestStore,
        IProgress<long>? bytesSent)
    {
        private int _uploaded;
        private int _skipped;
        private long _bytes;

        public int Uploaded => _uploaded;
        public int Skipped => _skipped;
        public long Bytes => _bytes;
        public List<ScanProblem> Problems { get; } = [];

        public async ValueTask UploadAssetAsync(string manifestPath, CancellationToken cancellationToken)
        {
            try
            {
                var manifest = ManifestJson.Deserialize(await File.ReadAllTextAsync(manifestPath, cancellationToken));

                // Order matters: an asset's manifest is only sent once everything it points to is in storage.
                if (!await UploadOriginalAsync(manifest, cancellationToken))
                {
                    return;
                }

                foreach (var derivative in manifest.Derivatives)
                {
                    var blobName = BlobLayout.DerivativeBlobName(manifest.Id, derivative);
                    await UploadWorkFileAsync(
                        containers.Derivatives, blobName, Path.Combine(workFolder, derivative.Path), derivative.ContentType, cancellationToken);
                }

                await UploadManifestAsync(manifest, manifestPath, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RequestFailedException or System.Text.Json.JsonException)
            {
                AddProblem(manifestPath, ex.Message);
            }
        }

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
        private async Task UploadManifestAsync(Manifest manifest, string path, CancellationToken cancellationToken)
        {
            var file = new FileInfo(path);
            var blobName = BlobLayout.ManifestBlobName(manifest.Id);
            var fingerprint = $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
            if (state.IsUploaded(BlobLayout.ManifestsContainer, blobName, fingerprint))
            {
                Interlocked.Increment(ref _skipped);
                return;
            }

            var result = await manifestStore.WriteMergedAsync(containers.Manifests.GetBlobClient(blobName), manifest, cancellationToken);
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
