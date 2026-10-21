using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Pipeline.Faces;

/// <summary>
/// Adds faces to a manifest that has none yet, or only from an older face pipeline, from the same derivative as the CLIP embedding: the 1280 px preview
/// of a photo or the poster of a video (D-042, D-047). Like an embedding, faces are optional data: a failure is
/// reported, never thrown (only cancellation is), and the next run with <c>--faces</c> tries again.
/// </summary>
public static class ManifestFaces
{
    /// <summary>Box and score digits kept in the manifest: a ten-thousandth of a 1280 px preview is a tenth of a pixel.</summary>
    public const int Decimals = 4;

    /// <summary>
    /// The face pipeline written to <c>facesVersion</c> (D-047). Bump it with any change to the face models or to how
    /// faces are found, aligned or embedded, so the new faces replace the old ones when manifests merge.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Looks for the faces of <paramref name="manifest"/> in its derivative under <paramref name="workFolder"/>; the time
    /// they were found, from <paramref name="clock"/> (the system clock by default), is written to <c>facesDetectedAt</c> (D-081).
    /// </summary>
    public static EmbeddingOutcome TryAdd(Manifest manifest, string workFolder, IFaceDetector detector, TimeProvider? clock = null)
    {
        // Faces from an older face pipeline are looked for again and replaced, so a work folder catches up with a model
        // or preprocessing change without reading the drive; a newer version, from a newer build, is left alone.
        if (manifest.FacesProcessed && manifest.EffectiveFacesVersion >= CurrentVersion)
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
            // A vector that is not finite or has no length cannot be compared, so that face is left out (as D-043).
            var faces = detector.Detect(path).Where(HasDirection).Select(ToManifest).ToList();
            return new EmbeddingOutcome(
                EmbeddingStatus.Added, manifest with
                {
                    FacesProcessed = true,
                    Faces = faces,
                    FacesVersion = CurrentVersion,
                    // To the second, UTC: what a merge compares with an erase (D-081), and readable in the file.
                    FacesDetectedAt = Seconds((clock ?? TimeProvider.System).GetUtcNow()),
                });
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            return Failed(manifest, "Could not detect faces", ex);
        }
    }

    private static DateTimeOffset Seconds(DateTimeOffset time) =>
        new(time.UtcTicks - (time.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    public static ManifestFace ToManifest(DetectedFace face)
    {
        if (face.Embedding.Length != FaceDetector.Dimensions)
        {
            throw new EmbeddingException($"A face embedding has {face.Embedding.Length} values instead of {FaceDetector.Dimensions}.");
        }

        // Normalized here too, so a detector that skips its own check cannot store a vector grouping cannot use.
        var embedding = EmbeddingCodec.Normalized(face.Embedding);
        var left = Round(face.X);
        var top = Round(face.Y);
        // Clamped after rounding, so the box never reaches past the right or bottom edge.
        var width = Math.Min(Round(face.Width), Round(1 - left));
        var height = Math.Min(Round(face.Height), Round(1 - top));
        return new ManifestFace([left, top, width, height], Round(face.Score), EmbeddingCodec.Encode(embedding));
    }

    // Only the direction is judged here: a vector of the wrong length is a model fault, which ToManifest turns into a
    // failure of the whole file instead of silently dropping every face.
    private static bool HasDirection(DetectedFace face) =>
        face.Embedding.Length != FaceDetector.Dimensions || EmbeddingCodec.TryNormalize(face.Embedding.ToArray());

    // The one rounding of box and score values, so every stored value rounds the same way.
    private static double Round(double value) => double.IsFinite(value)
        ? Math.Round(Math.Clamp(value, 0, 1), Decimals, MidpointRounding.AwayFromZero)
        : throw new EmbeddingException($"A face has a box or score that is not finite ({value}).");

    private static EmbeddingOutcome Failed(Manifest? manifest, string what, Exception ex) =>
        new(EmbeddingStatus.Failed, manifest, $"{what}: {ex.Message}", $"{what} ({ex.GetType().Name})");
}
