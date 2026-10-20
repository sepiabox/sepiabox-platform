using Sepiabox.Pipeline.Faces;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Pipeline.Embeddings;

/// <param name="Embedding">What happened to the embedding; null when there was no embedder, or when the failure to
/// read or write the manifest is reported on the faces instead.</param>
/// <param name="Faces">What happened to the faces; null when there was no detector, or when the failure to read or
/// write the manifest is reported on the embedding instead.</param>
public sealed record WorkFolderOutcome(EmbeddingOutcome? Embedding, EmbeddingOutcome? Faces);

/// <summary>
/// Adds what an unchanged file's manifest is missing, its embedding and its faces, from the work folder: the manifest
/// is read once, both are added in memory and it is written once, only when something was added. A failure to read or
/// write the manifest is reported once, on the embedding when there is an embedder.
/// </summary>
public static class WorkFolderManifest
{
    public static WorkFolderOutcome TryAdd(string manifestPath, string workFolder, IImageEmbedder? embedder, IFaceDetector? faces)
    {
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
            return Once(embedder is not null, Failed(null, "Could not read its manifest", ex));
        }

        var embedding = embedder is null ? null : ManifestEmbeddings.TryAdd(manifest, workFolder, embedder);
        var current = embedding?.Manifest ?? manifest;
        var found = faces is null ? null : ManifestFaces.TryAdd(current, workFolder, faces);
        current = found?.Manifest ?? current;
        if (embedding?.Status != EmbeddingStatus.Added && found?.Status != EmbeddingStatus.Added)
        {
            return new WorkFolderOutcome(embedding, found);
        }

        try
        {
            ManifestJson.WriteFile(manifestPath, current);
            return new WorkFolderOutcome(embedding, found);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            // Nothing was stored, so neither counts as added; the next run tries both again.
            var failed = Failed(manifest, "Could not write its manifest", ex);
            return Once(embedding?.Status == EmbeddingStatus.Added, failed, embedding, found);
        }
    }

    private static WorkFolderOutcome Once(bool onEmbedding, EmbeddingOutcome failure, EmbeddingOutcome? embedding = null, EmbeddingOutcome? faces = null) =>
        onEmbedding
            ? new WorkFolderOutcome(failure, faces?.Status == EmbeddingStatus.Added ? null : faces)
            : new WorkFolderOutcome(embedding?.Status == EmbeddingStatus.Added ? null : embedding, failure);

    // The exception's type, unlike its message, never names the file, so it groups failures of the same kind.
    private static EmbeddingOutcome Failed(Manifest? manifest, string what, Exception ex) =>
        new(EmbeddingStatus.Failed, manifest, $"{what}: {ex.Message}", $"{what} ({ex.GetType().Name})");
}
