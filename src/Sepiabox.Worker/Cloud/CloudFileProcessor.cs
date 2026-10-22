using ImageMagick;
using Microsoft.Extensions.Logging;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Faces;
using Sepiabox.Pipeline.Imaging;
using Sepiabox.Pipeline.Incoming;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
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
/// <param name="originalsCold">Whether the original is copied to the Cold tier: true in Azure, false on Azurite, which does not know it (D-004).</param>
public sealed class CloudFileProcessor(
    IBlobStore store,
    ManifestStore manifests,
    IVideoProcessor? video,
    IImageEmbedder? embedder,
    IFaceDetector? faces,
    TimeProvider clock,
    ILogger logger,
    bool originalsCold = false,
    IVideoProcessor? referenceVideo = null)
{
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
        var source = new ManifestSource(incoming.SourceType, incoming.SourceName, incoming.Path, incoming.ModifiedAt ?? info.LastModified, incoming.Url);
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
                var path = Path.Combine(work, derivative.Path);
                await store.WriteAsync(
                    BlobLayout.DerivativesContainer, BlobLayout.DerivativeBlobName(sha256, derivative.Path),
                    BinaryData.FromBytes(await File.ReadAllBytesAsync(path, cancellationToken)), derivative.ContentType,
                    BlobWriteCondition.Always, cancellationToken);
            }
        }

        if (withoutSound is not null)
        {
            Log.IncomingWithoutSound(logger, blobName, withoutSound);
        }

        if (!reference)
        {
            // On the store's side, so a video's gigabytes never pass through the worker; replaces the same bytes if it was there.
            await store.CopyAsync(BlobLayout.IncomingContainer, blobName, BlobLayout.OriginalsContainer, sha256, originalsCold, cancellationToken);
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

    private Manifest Describe(string sha256, MediaKind kind, IncomingFile incoming, BlobInfo info, string sourcePath, ManifestSource source, bool reference)
    {
        var metadata = MetadataReader.Read(sourcePath);
        return new Manifest
        {
            Id = sha256,
            Kind = kind,
            SizeBytes = info.Length,
            ContentType = MediaTypes.ContentType(incoming.Path),
            // The file's own date when plausible, otherwise one from its path in the cloud source (D-046).
            Captured = CaptureDate.Resolve(metadata.RawCapturedAt, incoming.Path, clock),
            Location = metadata.Location,
            Camera = metadata.Camera,
            Width = metadata.Width,
            Height = metadata.Height,
            DurationSeconds = metadata.DurationSeconds,
            Sources = [source],
            // Written only when false, as the schema says (D-088).
            OriginalStored = reference ? false : null,
        };
    }

    // The same rules as `process` (AddDerivatives there): previews are larger when the original is not kept (D-088), and a
    // file with a video extension that holds no video is left out (D-045).
    private (Manifest Manifest, CloudFileResult? Outcome) MakeDerivatives(
        Manifest manifest, string sourcePath, string work, bool reference, CancellationToken cancellationToken, out string? withoutSound)
    {
        withoutSound = null;
        var folder = $"derivatives/{manifest.Id}";
        try
        {
            if (manifest.Kind == MediaKind.Photo)
            {
                var sizes = reference ? ThumbnailGenerator.IndexOnlyPhotoSizes : ThumbnailGenerator.PhotoSizes;
                return (manifest with { Derivatives = ThumbnailGenerator.Generate(sourcePath, work, folder, sizes) }, null);
            }

            if (TrackerModule.IsTrackerModule(sourcePath))
            {
                return (manifest, new CloudFileResult(CloudFileStatus.LeftOut, "a tracker-music module, not a video"));
            }

            var processor = reference ? referenceVideo ?? video : video;
            if (processor is null)
            {
                // The worker's image has FFmpeg; without it every video would fail the same way, so the message is not
                // completed: it is delivered again and finally dead-lettered, where someone sees it.
                throw new InvalidOperationException("FFmpeg is not available, so a video cannot be processed.");
            }

            var probe = processor.Probe(sourcePath, cancellationToken);
            var generated = processor.Generate(sourcePath, work, folder, probe, cancellationToken);
            withoutSound = generated.WithoutSoundBecause;
            return (manifest with
            {
                Width = probe.Width,
                Height = probe.Height,
                DurationSeconds = probe.DurationSeconds ?? manifest.DurationSeconds,
                Location = manifest.Location ?? probe.Location,
                Derivatives = generated.Derivatives,
            }, null);
        }
        catch (NoVideoStreamException ex)
        {
            return (manifest, new CloudFileResult(CloudFileStatus.LeftOut, $"no video in it ({ex.Reason})"));
        }
        catch (Exception ex) when (ex is MagickException or VideoProcessingException)
        {
            var what = manifest.Kind == MediaKind.Photo ? "thumbnails" : "the poster and video version";
            return (manifest, new CloudFileResult(CloudFileStatus.Problem, $"Could not make {what}: {ex.Message}"));
        }
    }

    // Embedding, sharpness and faces from the derivatives just made, as `process` does; a failure of one is logged and
    // leaves it out, never the file.
    private Manifest AddSteps(Manifest manifest, string work)
    {
        if (embedder is not null)
        {
            var outcome = ManifestEmbeddings.TryAdd(manifest, work, embedder);
            manifest = Applied(manifest, outcome);
        }

        if (manifest.Kind == MediaKind.Photo)
        {
            manifest = Applied(manifest, ManifestQualityStep.TryAdd(manifest, work));
        }

        if (faces is not null)
        {
            manifest = Applied(manifest, ManifestFaces.TryAdd(manifest, work, faces, clock));
        }

        return manifest;
    }

    private Manifest Applied(Manifest manifest, EmbeddingOutcome? outcome)
    {
        if (outcome?.Problem is { } problem)
        {
            Log.IncomingStepLeftOut(logger, manifest.Id, problem);
        }

        return outcome?.Manifest ?? manifest;
    }

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
