using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Faces;
using Sepiabox.Pipeline.Imaging;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Processing;

/// <summary>The optional steps added to a new file's manifest from the derivatives just made (S-065).</summary>
public enum ManifestStep
{
    Embedding,
    Quality,
    Faces,
}

/// <summary>
/// Runs one optional step: calls <c>add</c>, and decides what a failure or a problem means to the caller (<c>process</c> turns an
/// exception into a warning for the file and counts what was added; the cloud worker logs what was left out). Returns the
/// step's outcome, or null when it did not run.
/// </summary>
public delegate EmbeddingOutcome? StepRunner(ManifestStep step, Func<EmbeddingOutcome?> add);

/// <summary>
/// The CLIP embedding, a photo's sharpness and the faces, added to a manifest in that order from the derivatives in
/// <c>workFolder</c> (S-065, D-118): the one sequence <c>process</c> and the cloud worker share. How each step's outcome is
/// recorded is the caller's, through the <see cref="StepRunner"/>.
/// </summary>
public static class ManifestSteps
{
    public static Manifest Add(
        Manifest manifest, string workFolder, IImageEmbedder? embedder, IFaceDetector? faces, TimeProvider clock, StepRunner run)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(run);
        if (embedder is not null)
        {
            var current = manifest;
            manifest = run(ManifestStep.Embedding, () => ManifestEmbeddings.TryAdd(current, workFolder, embedder))?.Manifest ?? manifest;
        }

        if (manifest.Kind == MediaKind.Photo)
        {
            var current = manifest;
            manifest = run(ManifestStep.Quality, () => ManifestQualityStep.TryAdd(current, workFolder))?.Manifest ?? manifest;
        }

        if (faces is not null)
        {
            var current = manifest;
            manifest = run(ManifestStep.Faces, () => ManifestFaces.TryAdd(current, workFolder, faces, clock))?.Manifest ?? manifest;
        }

        return manifest;
    }
}
