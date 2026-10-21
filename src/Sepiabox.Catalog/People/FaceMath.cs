namespace Sepiabox.Catalog.People;

/// <summary>The arithmetic of grouping faces (S-019): similarity, centroids and box overlap.</summary>
public static class FaceMath
{
    /// <summary>Cosine similarity; 0 when either vector has no length or their sizes differ.</summary>
    public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0;
        }

        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            aa += (double)a[i] * a[i];
            bb += (double)b[i] * b[i];
        }

        return aa == 0 || bb == 0 ? 0 : dot / Math.Sqrt(aa * bb);
    }

    /// <summary>
    /// The L2-normalized mean of <paramref name="embeddings"/>, which are themselves normalized, so it points where
    /// they point on average; null for none, or when they cancel out.
    /// </summary>
    public static float[]? Centroid(IEnumerable<float[]> embeddings)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        double[]? sum = null;
        foreach (var embedding in embeddings)
        {
            sum ??= new double[embedding.Length];
            if (embedding.Length != sum.Length)
            {
                throw new ArgumentException("Embeddings of different sizes cannot be averaged.", nameof(embeddings));
            }

            for (var i = 0; i < embedding.Length; i++)
            {
                sum[i] += embedding[i];
            }
        }

        if (sum is null)
        {
            return null;
        }

        var length = Math.Sqrt(sum.Sum(v => v * v));
        return length == 0 || !double.IsFinite(length) ? null : [.. sum.Select(v => (float)(v / length))];
    }

    /// <summary>Intersection over union of two <c>[left, top, width, height]</c> boxes; 0 when they do not overlap.</summary>
    public static double IoU(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var width = Math.Min(a[0] + a[2], b[0] + b[2]) - Math.Max(a[0], b[0]);
        var height = Math.Min(a[1] + a[3], b[1] + b[3]) - Math.Max(a[1], b[1]);
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        var intersection = width * height;
        var union = (a[2] * a[3]) + (b[2] * b[3]) - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}
