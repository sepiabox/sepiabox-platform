using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Pipeline.Faces;

/// <summary>
/// Adds faces to a manifest that has none yet, from the same derivative as the CLIP embedding: the 1280 px preview
/// of a photo or the poster of a video (D-042, D-047). Like an embedding, faces are optional data: a failure is
/// reported, never thrown (only cancellation is), and the next run with <c>--faces</c> tries again.
/// </summary>
public static class ManifestFaces
{
    /// <summary>Box and score digits kept in the manifest: a ten-thousandth of a 1280 px preview is a tenth of a pixel.</summary>
    public const int Decimals = 4;

    public static EmbeddingOutcome TryAdd(Manifest manifest, string workFolder, IFaceDetector detector)
    {
        if (manifest.FacesProcessed)
        {
            return new EmbeddingOutcome(EmbeddingStatus.Present, manifest);
        }

        var name = ManifestEmbeddings.SourceDerivative(manifest.Kind);
        var derivative = manifest.Derivatives.FirstOrDefault(d => d.Name == name);
        if (derivative is null)
        {
            return new EmbeddingOutcome(EmbeddingStatus.NotListed, manifest);
        }

        var path = Path.Combine(workFolder, derivative.Path);
        if (!File.Exists(path))
        {
            return new EmbeddingOutcome(EmbeddingStatus.NoDerivative, manifest);
        }

        try
        {
            var faces = detector.Detect(path).Select(ToManifest).ToList();
            return new EmbeddingOutcome(EmbeddingStatus.Added, manifest with { FacesProcessed = true, Faces = faces });
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            return Failed(manifest, "Could not detect faces", ex);
        }
    }

    /// <summary>The same for a manifest file in the work folder, which is rewritten only when faces were added.</summary>
    public static EmbeddingOutcome TryAddToFile(string manifestPath, string workFolder, IFaceDetector detector)
    {
        Manifest manifest;
        try
        {
            manifest = ManifestJson.Deserialize(File.ReadAllText(manifestPath));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new EmbeddingOutcome(EmbeddingStatus.ManifestMissing, null);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            return Failed(null, "Could not read its manifest", ex);
        }

        var outcome = TryAdd(manifest, workFolder, detector);
        if (outcome.Status != EmbeddingStatus.Added)
        {
            return outcome;
        }

        try
        {
            ManifestJson.WriteFile(manifestPath, outcome.Manifest!);
            return outcome;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            return Failed(manifest, "Could not write its manifest", ex);
        }
    }

    public static ManifestFace ToManifest(DetectedFace face)
    {
        if (face.Embedding.Length != FaceDetector.Dimensions)
        {
            throw new EmbeddingException($"A face embedding has {face.Embedding.Length} values instead of {FaceDetector.Dimensions}.");
        }

        // Normalized here too, so a detector that skips its own check cannot store a vector grouping cannot use.
        var embedding = EmbeddingCodec.Normalized(face.Embedding);
        return new ManifestFace(
            [Round(face.X), Round(face.Y), Round(face.Width), Round(face.Height)],
            Round(face.Score),
            EmbeddingCodec.Encode(embedding));
    }

    private static double Round(double value) => double.IsFinite(value)
        ? Math.Round(Math.Clamp(value, 0, 1), Decimals, MidpointRounding.AwayFromZero)
        : throw new EmbeddingException($"A face has a box or score that is not finite ({value}).");

    private static EmbeddingOutcome Failed(Manifest? manifest, string what, Exception ex) =>
        new(EmbeddingStatus.Failed, manifest, $"{what}: {ex.Message}", $"{what} ({ex.GetType().Name})");
}
