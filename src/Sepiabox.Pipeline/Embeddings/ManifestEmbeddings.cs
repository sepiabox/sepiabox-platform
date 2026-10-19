using System.Text.Json.Nodes;
using Sepiabox.Pipeline.Imaging;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Video;

namespace Sepiabox.Pipeline.Embeddings;

public enum EmbeddingStatus
{
    /// <summary>The manifest already had an embedding for the model.</summary>
    Present,

    Added,

    /// <summary>The manifest lists a preview or poster, but its file is not in the work folder.</summary>
    NoDerivative,

    /// <summary>The manifest lists no preview or poster, such as a video still waiting for FFmpeg.</summary>
    NotListed,

    /// <summary>The manifest file is not in the work folder.</summary>
    ManifestMissing,

    /// <summary>The manifest could not be read or written, or the image could not be embedded; see the problem.</summary>
    Failed,
}

/// <param name="Status">What happened.</param>
/// <param name="Manifest">The manifest, with the embedding when it was added; null when it could not be read.</param>
/// <param name="Problem">Why it failed, when <paramref name="Status"/> is <see cref="EmbeddingStatus.Failed"/>.</param>
public sealed record EmbeddingOutcome(EmbeddingStatus Status, Manifest? Manifest, string? Problem = null);

/// <summary>
/// Adds a missing embedding to a manifest from the derivative the web shows: the 1280 px preview of a photo
/// or the poster of a video (D-042). It never reads the original, so it also works on a work folder whose
/// drive is no longer connected. A failure is reported, never thrown (only cancellation is): an embedding is
/// optional and retried on the next run.
/// </summary>
public static class ManifestEmbeddings
{
    public static string SourceDerivative(MediaKind kind) =>
        kind == MediaKind.Video ? VideoProcessor.PosterName : ThumbnailGenerator.PreviewName;

    public static EmbeddingOutcome TryAdd(Manifest manifest, string workFolder, IImageEmbedder embedder)
    {
        if (manifest.Embeddings.ContainsKey(embedder.ModelKey))
        {
            return new EmbeddingOutcome(EmbeddingStatus.Present, manifest);
        }

        var name = SourceDerivative(manifest.Kind);
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
            var vector = embedder.Embed(path);
            if (vector.Length != embedder.Dimensions)
            {
                throw new EmbeddingException($"The model returned {vector.Length} values instead of {embedder.Dimensions}.");
            }

            // Checked here too, so an embedder that skips its own check cannot store a vector search cannot use.
            EmbeddingCodec.Normalize(vector);

            var embeddings = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
            foreach (var (key, value) in manifest.Embeddings)
            {
                embeddings[key] = value.DeepClone();
            }

            embeddings[embedder.ModelKey] = JsonValue.Create(EmbeddingCodec.Encode(vector));
            return new EmbeddingOutcome(EmbeddingStatus.Added, manifest with { Embeddings = embeddings });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Whatever the embedder throws, the asset is still described; only its embedding is missing.
            return new EmbeddingOutcome(EmbeddingStatus.Failed, manifest, $"Could not compute the {embedder.ModelKey} embedding: {ex.Message}");
        }
    }

    /// <summary>The same for a manifest file in the work folder, which is rewritten only when an embedding was added.</summary>
    public static EmbeddingOutcome TryAddToFile(string manifestPath, string workFolder, IImageEmbedder embedder)
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new EmbeddingOutcome(EmbeddingStatus.Failed, null, $"Could not read its manifest: {ex.Message}");
        }

        var outcome = TryAdd(manifest, workFolder, embedder);
        if (outcome.Status != EmbeddingStatus.Added)
        {
            return outcome;
        }

        try
        {
            ManifestJson.WriteFile(manifestPath, outcome.Manifest!);
            return outcome;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new EmbeddingOutcome(EmbeddingStatus.Failed, manifest, $"Could not write its manifest: {ex.Message}");
        }
    }
}
