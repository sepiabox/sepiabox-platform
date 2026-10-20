using System.Buffers.Binary;

namespace Sepiabox.Pipeline.Embeddings;

/// <summary>How an embedding travels in a manifest: base64 of float32 little-endian values.</summary>
public static class EmbeddingCodec
{
    public static string Encode(ReadOnlySpan<float> values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), values[i]);
        }

        return Convert.ToBase64String(bytes);
    }

    /// <summary>Throws <see cref="FormatException"/> for text that is not base64 of whole float32 values.</summary>
    public static float[] Decode(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        if (bytes.Length % sizeof(float) != 0)
        {
            throw new FormatException($"An embedding of {bytes.Length} bytes is not a whole number of float32 values.");
        }

        var values = new float[bytes.Length / sizeof(float)];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float)));
        }

        return values;
    }

    /// <summary>
    /// Scales <paramref name="values"/> to length 1 in place, so cosine similarity is a dot product. A vector
    /// with a value that is not finite, or with no length, has no direction and is refused.
    /// </summary>
    public static void Normalize(Span<float> values)
    {
        if (TryScale(values) is { } problem)
        {
            throw new EmbeddingException(problem);
        }
    }

    /// <summary>
    /// Like <see cref="Normalize"/>, but false instead of an exception for a vector that cannot be normalized, which is
    /// then left unchanged.
    /// </summary>
    public static bool TryNormalize(Span<float> values) => TryScale(values) is null;

    // The one normalization: divides by the double-precision norm, or says why it cannot and changes nothing.
    private static string? TryScale(Span<float> values)
    {
        double sum = 0;
        foreach (var value in values)
        {
            if (!float.IsFinite(value))
            {
                return $"The embedding has a value that is not finite ({value}).";
            }

            sum += (double)value * value;
        }

        if (sum == 0)
        {
            return "The embedding is a zero vector, which has no direction.";
        }

        var norm = Math.Sqrt(sum);
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (float)(values[i] / norm);
        }

        return null;
    }

    /// <summary>A normalized copy of <paramref name="values"/>; see <see cref="Normalize"/>.</summary>
    public static float[] Normalized(ReadOnlySpan<float> values)
    {
        var copy = values.ToArray();
        Normalize(copy);
        return copy;
    }

    public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Embeddings of different lengths cannot be compared.");
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            normA += (double)a[i] * a[i];
            normB += (double)b[i] * b[i];
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
