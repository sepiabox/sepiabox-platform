using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Imaging;

/// <summary>
/// Adds a photo's missing <c>quality</c> (S-027, D-098) from the derivative the web shows, as the embedding is: the
/// largest preview the manifest lists, never the original, so it works on a work folder whose drive is no longer
/// connected and on one processed before this existed. A failure is reported, never thrown (only cancellation is): the
/// measure is optional and made again on the next run.
/// </summary>
public static class ManifestQualityStep
{
    public static EmbeddingOutcome TryAdd(Manifest manifest, string workFolder)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        // Photos only: a video's poster says little about how sharp it was, and bursts are of photos.
        if (manifest.Kind != MediaKind.Photo || manifest.Quality is not null)
        {
            return new EmbeddingOutcome(EmbeddingStatus.Present, manifest);
        }

        var derivative = ManifestEmbeddings.SourceDerivative(manifest);
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
            var quality = QualityAnalyzer.Analyze(path);
            return new EmbeddingOutcome(
                EmbeddingStatus.Added,
                manifest with { Quality = new ManifestQuality(Math.Round(quality.Sharpness, 2), quality.DHash) });
        }
        catch (Exception ex) when (ex is ImageMagick.MagickException or IOException or InvalidOperationException)
        {
            return new EmbeddingOutcome(EmbeddingStatus.Failed, manifest, $"Could not measure its sharpness: {ex.Message}", $"Could not measure its sharpness ({ex.GetType().Name})");
        }
    }
}
