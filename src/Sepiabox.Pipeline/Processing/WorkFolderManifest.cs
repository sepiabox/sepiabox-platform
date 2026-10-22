using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Faces;
using Sepiabox.Pipeline.Imaging;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Pipeline.Processing;

/// <param name="Embedding">What happened to the embedding; null when there was no embedder, or when the failure to
/// read or write the manifest is reported on the faces instead.</param>
/// <param name="Faces">What happened to the faces; null when there was no detector, or when the failure to read or
/// write the manifest is reported on the embedding instead.</param>
public sealed record WorkFolderOutcome(EmbeddingOutcome? Embedding, EmbeddingOutcome? Faces)
{
    /// <summary>What happened to the photo's sharpness and hash (S-027); null when they were not asked for, or when the failure
    /// to read or write the manifest is reported on the embedding or the faces instead.</summary>
    public EmbeddingOutcome? Quality { get; init; }
}

/// <summary>
/// Adds what an unchanged file's manifest is missing, its embedding and its faces, from the work folder: the manifest
/// is read once, both are added in memory and it is written once, only when something was added. A failure to read
/// or write the manifest is reported once: a read failure on the embedding when there is an embedder, a write failure
/// on the embedding when one was added; otherwise on the faces.
/// </summary>
public static class WorkFolderManifest
{
    /// <param name="manifestPath">The work-folder manifest to add to.</param>
    /// <param name="workFolder">The work folder its derivatives are in.</param>
    /// <param name="embedder">The CLIP image model, or none.</param>
    /// <param name="faces">The face detector, or none.</param>
    /// <param name="clock">When faces are found (D-081); the system clock when null.</param>
    /// <param name="quality">Also measure a photo's sharpness and hash from its preview (S-027, D-098).</param>
    public static WorkFolderOutcome TryAdd(
        string manifestPath, string workFolder, IImageEmbedder? embedder, IFaceDetector? faces, TimeProvider? clock = null, bool quality = false)
    {
        if (embedder is null && faces is null && !quality)
        {
            return new WorkFolderOutcome(null, null);
        }

        Manifest manifest;
        try
        {
            manifest = ManifestJson.Deserialize(File.ReadAllText(manifestPath));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            var missing = new EmbeddingOutcome(EmbeddingStatus.ManifestMissing, null);
            return new WorkFolderOutcome(embedder is null ? null : missing, faces is null ? null : missing);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            var failure = Failed(null, "Could not read its manifest", ex);
            return embedder is not null || faces is null
                ? new WorkFolderOutcome(failure, null)
                : new WorkFolderOutcome(null, failure);
        }

        var embedding = embedder is null ? null : ManifestEmbeddings.TryAdd(manifest, workFolder, embedder);
        var current = embedding?.Manifest ?? manifest;
        var found = faces is null ? null : ManifestFaces.TryAdd(current, workFolder, faces, clock);
        current = found?.Manifest ?? current;
        var measured = quality ? ManifestQualityStep.TryAdd(current, workFolder) : null;
        current = measured?.Manifest ?? current;
        if (!IsAdded(embedding) && !IsAdded(found) && !IsAdded(measured))
        {
            return new WorkFolderOutcome(embedding, found) { Quality = measured };
        }

        try
        {
            ManifestJson.WriteFile(manifestPath, current);
            return new WorkFolderOutcome(embedding, found) { Quality = measured };
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            // Nothing was stored, so the measure is not "added" either; the failure is reported on the embedding or the faces,
            // or, when the sharpness is all that was asked for, on the measure itself.
            var failure = Failed(manifest, "Could not write its manifest", ex);
            return embedding is null && found is null ? new WorkFolderOutcome(null, null) { Quality = failure } : WriteFailed(failure, embedding, found);
        }
    }

    private static bool IsAdded(EmbeddingOutcome? outcome) => outcome?.Status == EmbeddingStatus.Added;

    // Nothing was stored, so neither counts as added and the next run tries both again. The failure is reported on
    // the embedding when one was added, otherwise on the faces; the other side keeps what it was, unless it was
    // "added", which did not happen.
    private static WorkFolderOutcome WriteFailed(EmbeddingOutcome failure, EmbeddingOutcome? embedding, EmbeddingOutcome? faces) =>
        IsAdded(embedding)
            ? new WorkFolderOutcome(failure, IsAdded(faces) ? null : faces)
            : new WorkFolderOutcome(embedding, failure);

    // The exception's type, unlike its message, never names the file, so it groups failures of the same kind.
    private static EmbeddingOutcome Failed(Manifest? manifest, string what, Exception ex) =>
        new(EmbeddingStatus.Failed, manifest, $"{what}: {ex.Message}", $"{what} ({ex.GetType().Name})");
}
