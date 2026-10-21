using Sepiabox.Pipeline.Embeddings;

namespace Sepiabox.Tests.Shared;

/// <summary>
/// An embedder that needs no model: it returns a fixed unit vector, or throws what <see cref="Failure"/>
/// makes, so tests can drive how processing reacts to an embedding that works or fails.
/// </summary>
internal sealed class FakeEmbedder(string modelKey = ClipImageEmbedder.ModelKey, int dimensions = ClipImageEmbedder.Dimensions) : IImageEmbedder
{
    public string ModelKey { get; } = modelKey;

    public int Dimensions { get; } = dimensions;

    /// <summary>When set, <see cref="Embed"/> throws what it returns for the path.</summary>
    public Func<string, Exception?>? Failure { get; set; }

    /// <summary>When set, <see cref="Embed"/> returns this instead of a unit vector.</summary>
    public float[]? Output { get; set; }

    /// <summary>When set, <see cref="Embed"/> returns this as it is, without the checks a real embedder makes.</summary>
    public float[]? RawOutput { get; set; }

    public List<string> Embedded { get; } = [];

    public float[] Embed(string path)
    {
        if (Failure?.Invoke(path) is { } failure)
        {
            throw failure;
        }

        // process runs files in parallel (D-078).
        lock (Embedded)
        {
            Embedded.Add(path);
        }

        if (RawOutput is not null)
        {
            return RawOutput;
        }

        if (Output is not null)
        {
            var output = (float[])Output.Clone();
            EmbeddingCodec.Normalize(output);
            return output;
        }

        var vector = new float[Dimensions];
        vector[0] = 1f;
        return vector;
    }
}
