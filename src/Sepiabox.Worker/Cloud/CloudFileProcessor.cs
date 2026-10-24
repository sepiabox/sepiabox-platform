using Microsoft.Extensions.Logging;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Faces;
using Sepiabox.Pipeline.Incoming;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Processing;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Pipeline.Video;
using Sepiabox.Storage;

namespace Sepiabox.Worker.Cloud;

public enum CloudFileStatus
{
    /// <summary>Described, its derivatives and manifest written, its original moved (custody) and the incoming blob deleted.</summary>
    Processed,

    /// <summary>The blob was no longer in <c>incoming</c>: an earlier delivery of the same message processed it.</summary>
    AlreadyDone,

    /// <summary>Not a photo or video, or a file with a video extension that holds no video: no manifest, the blob deleted, the reason logged.</summary>
    LeftOut,

    /// <summary>It could not be processed (no usable metadata, a file that cannot be decoded, a manifest that cannot be written); the blob stays in <c>incoming</c>.</summary>
    Problem,
}

public sealed record CloudFileResult(CloudFileStatus Status, string? Reason = null, string? AssetId = null);

/// <summary>
/// <c>process --from-queue</c> for one file (S-036, D-107): what <c>sepiabox-ingest process</c> and <c>upload</c> do on a PC,
/// done by the worker for a file that arrived in the <c>incoming</c> container from a cloud source. It hashes the file,
/// reads its metadata, makes the thumbnails, previews or poster and 720p version (and the CLIP embedding, sharpness and,
/// when asked, faces), writes the derivatives, then the original (custody mode only, to Cold) and the manifest, through
/// <see cref="ManifestStore"/> like any writer (D-036), and deletes the incoming blob last, so a failure at any step leaves
/// the file to be processed again, which changes nothing it already wrote.
/// </summary>
/// <param name="store">The blobs (D-051).</param>
/// <param name="manifests">Writes the manifest, merging with one already there.</param>
/// <param name="video">FFmpeg's processor; null when FFmpeg is not installed, which makes a video a failure of the message.</param>
/// <param name="embedder">CLIP's image side; null leaves the embedding out (a later import-time sweep or <c>process</c> can add it).</param>
/// <param name="faces">The face detector, only when the library's face recognition is on (D-047, S-022); otherwise null.</param>
/// <param name="clock">Dates the faces (D-081) and the path dates (D-046).</param>
/// <param name="logger">Reports what is left out and what could not be done.</param>
/// <param name="referenceVideo">The processor for reference mode, whose video version is 1080p because the original is not kept (D-088); null uses <paramref name="video"/>.</param>
/// <param name="originalsCold">Whether the original is stored in the Cold tier: true in Azure, false on Azurite, which does not know it (D-004).</param>
/// <param name="maxFileBytes">The largest file taken, which bounds the job's temporary disk; a larger one is a problem (D-107).</param>
public sealed class CloudFileProcessor(
    IBlobStore store,
    ManifestStore manifests,
    IVideoProcessor? video,
    IImageEmbedder? embedder,
    IFaceDetector? faces,
    TimeProvider clock,
    ILogger logger,
    bool originalsCold = false,
    IVideoProcessor? referenceVideo = null,
    long maxFileBytes = CloudFileProcessor.MaxFileBytes)
{
    /// <summary>
    /// The largest file taken: 3 GiB. The job's ephemeral disk is about 8 GiB at 2 vCPU, and the file is downloaded once, is
    /// read by FFmpeg and has its derivatives beside it. A larger file stays in <c>incoming</c> as a problem for someone to look at.
    /// </summary>
    public const long MaxFileBytes = 3L * 1024 * 1024 * 1024;

    public async Task<CloudFileResult> ProcessAsync(string blobName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(blobName);
        var work = Path.Combine(Path.GetTempPath(), "sepiabox-incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            return await RunAsync(blobName, work, cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file held by a scan: the temporary folder is left for the system to clean, never a reason to fail.
                Log.IncomingWorkFolderNotDeleted(logger, work, ex);
            }
        }
    }

    private async Task<CloudFileResult> RunAsync(string blobName, string work, CancellationToken cancellationToken)
    {
        // Before the download, so that a file too large for the disk is never fetched.
        if (await store.GetSizeAsync(BlobLayout.IncomingContainer, blobName, cancellationToken) is not { } size)
        {
            Log.IncomingAlreadyDone(logger, blobName);
            return new CloudFileResult(CloudFileStatus.AlreadyDone);
        }

        if (size > maxFileBytes)
        {
            return Failed(blobName, null, TooLarge(size));
        }

        var downloaded = Path.Combine(work, "download.tmp");
        BlobInfo? info;
        await using (var target = new FileStream(downloaded, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            info = await store.DownloadToAsync(BlobLayout.IncomingContainer, blobName, target, cancellationToken);
        }

        if (info is null)
        {
            Log.IncomingAlreadyDone(logger, blobName);
            return new CloudFileResult(CloudFileStatus.AlreadyDone);
        }

        if (info.Length > maxFileBytes)
        {
            return Failed(blobName, null, TooLarge(info.Length));
        }

        // A connector that writes the blob and then its metadata (or streams it without the commit carrying them) is seen
        // by the event before the metadata is there: that passes, so the message is tried again rather than closed as a problem.
        if (info.Metadata.Count == 0)
        {
            throw new InvalidOperationException($"The incoming blob {blobName} has no metadata yet; it is tried again.");
        }

        if (!IncomingFile.TryRead(info.Metadata, out var incoming, out var metadataProblem))
        {
            Log.IncomingProblem(logger, blobName, metadataProblem);
            return new CloudFileResult(CloudFileStatus.Problem, metadataProblem);
        }

        var kind = MediaTypes.Classify(incoming.Path);
        if (kind == MediaKind.Other)
        {
            return await LeftOutAsync(blobName, null, "it is not a photo or video (by its extension)", cancellationToken);
        }

        // Named by the cloud path's extension: it is what says what the file is (as on a PC), and what the readers go by.
        var sourcePath = Path.Combine(work, "original" + Path.GetExtension(incoming.Path));
        File.Move(downloaded, sourcePath);
        var sha256 = MediaFile.Sha256Hex(sourcePath);
        var source = new ManifestSource(incoming.SourceType, incoming.SourceName, incoming.Path, incoming.ModifiedAt ?? info.LastModified, SourceUrl(incoming.Url));
        var reference = incoming.Mode == IncomingMode.Reference;
        var manifestBlob = BlobLayout.ManifestBlobName(sha256);

        // The same content again (a photo on the PC and in Drive, or a message delivered twice after a crash): its
        // derivatives are there, so only the source is added.
        var existing = await ReadManifestAsync(manifestBlob, cancellationToken);
        Manifest manifest;
        string? withoutSound = null;
        if (existing is { Derivatives.Count: > 0 })
        {
            manifest = Describe(sha256, kind, incoming, info, sourcePath, source, reference) with { Derivatives = [] };
        }
        else
        {
            var described = Describe(sha256, kind, incoming, info, sourcePath, source, reference);
            (manifest, var outcome) = MakeDerivatives(described, sourcePath, work, reference, cancellationToken, out withoutSound);
            if (outcome is { } problem)
            {
                return problem.Status == CloudFileStatus.LeftOut
                    ? await LeftOutAsync(blobName, sha256, problem.Reason!, cancellationToken)
                    : Failed(blobName, sha256, problem.Reason!);
            }

            manifest = AddSteps(manifest, work);
            foreach (var derivative in manifest.Derivatives)
            {
                await WriteDerivativeAsync(
                    BlobLayout.DerivativeBlobName(sha256, derivative.Path), Path.Combine(work, derivative.Path), derivative.ContentType, cancellationToken);
            }
        }

        if (withoutSound is not null)
        {
            Log.IncomingWithoutSound(logger, blobName, withoutSound);
        }

        if (!reference)
        {
            await StoreOriginalAsync(sha256, sourcePath, incoming.Path, cancellationToken);
        }

        // Last but the deletion: the manifest is what triggers the import, so everything it points to is stored first.
        var written = await manifests.WriteMergedAsync(BlobLayout.ManifestsContainer, manifest, cancellationToken);
        if (written.Status == ManifestWriteStatus.Problem)
        {
            return Failed(blobName, sha256, written.Problem ?? "the manifest could not be written");
        }

        await store.DeleteAsync(BlobLayout.IncomingContainer, blobName, cancellationToken);
        Log.IncomingProcessed(logger, blobName, sha256, reference ? "reference" : "custody");
        return new CloudFileResult(CloudFileStatus.Processed, AssetId: sha256);
    }

    /// <summary>The block size of a staged upload, as <c>upload</c> uses (D-030).</summary>
    public const int BlockSize = 8 * 1024 * 1024;

    // The original is written from the file that was hashed, never copied from the incoming blob by name: that name could have
    // been rewritten since the download, and `originals/<sha256>` must hold the bytes its name says (D-030). A blob there with
    // the right size is the same content (the name is its hash), so it is left alone, as `upload` leaves it.
    private async Task StoreOriginalAsync(string sha256, string localPath, string cloudPath, CancellationToken cancellationToken)
    {
        if (await store.GetSizeAsync(BlobLayout.OriginalsContainer, sha256, cancellationToken) == new FileInfo(localPath).Length)
        {
            return;
        }

        await UploadFileAsync(
            BlobLayout.OriginalsContainer, sha256, localPath, MediaTypes.ContentType(cloudPath),
            originalsCold ? BlobStorageClass.Cold : BlobStorageClass.Default, cancellationToken);
    }

    // The manifest takes an absolute https address without credentials within its length limit (the schema refuses anything else, which
    // would make the file a permanent problem): a connector's other address is left out, and the file is catalogued without it.
    internal static string? SourceUrl(string? url) =>
        url is { Length: > 0 and <= ManifestSource.MaxUrlLength }
        && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0
            ? url
            : null;

    private string TooLarge(long bytes) =>
        $"The file is {bytes / (1024 * 1024)} MiB, more than the {maxFileBytes / (1024 * 1024)} MiB the cloud worker takes; it stays in incoming.";

    // A file of less than a block (a thumbnail, a preview) is one write; a larger one (a video's version) is staged block by
    // block, so it is never held whole in memory (D-100).
    internal async Task WriteDerivativeAsync(string blobName, string path, string contentType, CancellationToken cancellationToken)
    {
        if (new FileInfo(path).Length < BlockSize)
        {
            await store.WriteAsync(
                BlobLayout.DerivativesContainer, blobName, BinaryData.FromBytes(await File.ReadAllBytesAsync(path, cancellationToken)),
                contentType, BlobWriteCondition.Always, cancellationToken);
            return;
        }

        await UploadFileAsync(BlobLayout.DerivativesContainer, blobName, path, contentType, BlobStorageClass.Default, cancellationToken);
    }

    private async Task UploadFileAsync(
        string container, string blobName, string path, string contentType, BlobStorageClass storageClass, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        await using var upload = store.BeginUpload(container, blobName);
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(BlockSize);
        try
        {
            int read;
            while ((read = await stream.ReadAtLeastAsync(buffer.AsMemory(0, BlockSize), BlockSize, throwOnEndOfStream: false, cancellationToken)) > 0)
            {
                await upload.AppendAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }

        await upload.CommitAsync(contentType, storageClass, cancellationToken);
    }

    private Manifest Describe(string sha256, MediaKind kind, IncomingFile incoming, BlobInfo info, string sourcePath, ManifestSource source, bool reference) =>
        // The date is the file's own when plausible, otherwise one from its path in the cloud source (D-046); the description
        // itself is shared with `process` (S-065, D-118).
        ManifestDescriber.Describe(new ManifestDescriber.Request(
            sha256, kind, info.Length, incoming.Path, MetadataReader.Read(sourcePath), source, IndexOnly: reference, clock));

    // The derivatives are made by the code `process` shares (S-065): previews are larger when the original is not kept (D-088),
    // and a file with a video extension that holds no video is left out (D-045).
    private (Manifest Manifest, CloudFileResult? Outcome) MakeDerivatives(
        Manifest manifest, string sourcePath, string work, bool reference, CancellationToken cancellationToken, out string? withoutSound)
    {
        withoutSound = null;
        var processor = manifest.Kind == MediaKind.Video && reference ? referenceVideo ?? video : video;
        if (manifest.Kind == MediaKind.Video && !TrackerModule.IsTrackerModule(sourcePath) && processor is null)
        {
            // The worker's image has FFmpeg; without it every video would fail the same way, so the message is not
            // completed: it is delivered again and finally dead-lettered, where someone sees it.
            throw new InvalidOperationException("FFmpeg is not available, so a video cannot be processed.");
        }

        var made = DerivativeMaker.Make(manifest, sourcePath, content: null, work, indexOnly: reference, processor, cancellationToken);
        withoutSound = made.WithoutSound;
        return made.Status switch
        {
            DerivativeStatus.Done => (made.Manifest, null),
            DerivativeStatus.NotVideo => (manifest, new CloudFileResult(
                CloudFileStatus.LeftOut,
                made.NoVideo == NoVideoReason.TrackerModule ? "a tracker-music module, not a video" : $"no video in it ({made.NoVideo})")),
            // A damaged photo and one that could not be made are the same to the worker: a problem of the message.
            _ => (manifest, new CloudFileResult(
                CloudFileStatus.Problem,
                $"Could not make {(manifest.Kind == MediaKind.Photo ? "thumbnails" : "the poster and video version")}: {made.Problem}")),
        };
    }

    // Embedding, sharpness and faces from the derivatives just made, in the order `process` uses (S-065); a failure of one is
    // logged and leaves it out, never the file.
    private Manifest AddSteps(Manifest manifest, string work) =>
        ManifestSteps.Add(
            manifest, work, embedder, faces, clock,
            (_, add) =>
            {
                var outcome = add();
                if (outcome?.Problem is { } problem)
                {
                    Log.IncomingStepLeftOut(logger, manifest.Id, problem);
                }

                return outcome;
            });

    private async Task<Manifest?> ReadManifestAsync(string manifestBlob, CancellationToken cancellationToken)
    {
        var blob = await store.ReadAsync(BlobLayout.ManifestsContainer, manifestBlob, cancellationToken);
        if (blob is null)
        {
            return null;
        }

        try
        {
            return ManifestJson.Deserialize(blob.Content.ToString());
        }
        catch (System.Text.Json.JsonException)
        {
            // Not readable: described again, and the merge reports it if it still cannot be read.
            return null;
        }
    }

    private async Task<CloudFileResult> LeftOutAsync(string blobName, string? assetId, string reason, CancellationToken cancellationToken)
    {
        Log.IncomingLeftOut(logger, blobName, reason);
        // The file is only a copy staged by a connector and the source still has it: nothing to catalogue, nothing to keep.
        await store.DeleteAsync(BlobLayout.IncomingContainer, blobName, cancellationToken);
        return new CloudFileResult(CloudFileStatus.LeftOut, reason, assetId);
    }

    private CloudFileResult Failed(string blobName, string? assetId, string reason)
    {
        Log.IncomingProblem(logger, blobName, reason);
        return new CloudFileResult(CloudFileStatus.Problem, reason, assetId);
    }
}
