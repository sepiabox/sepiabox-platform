using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Sepiabox.Pipeline.Manifests;

/// <summary>A face found in the asset (S-018).</summary>
/// <param name="Box">Left, top, width and height, relative to the upright derivative the face was found in (0–1). A
/// derivative keeps the picture's proportions after orientation, so the box is mapped with a derivative's width and
/// height, never the manifest's, which are the original's as stored.</param>
/// <param name="Score">The detector's confidence, 0–1.</param>
/// <param name="Embedding">The SFace vector: base64 of 128 float32 little-endian values, L2-normalized.</param>
public sealed record ManifestFace(IReadOnlyList<double> Box, double Score, string Embedding)
{
    private readonly IReadOnlyList<double> _box = Copy(Box);

    /// <summary>A read-only copy, so a face never changes after it is made, even through <c>with</c>.</summary>
    // First in the JSON, as in the schema, although declared here rather than in the parameter list.
    [JsonPropertyOrder(-1)]
    public IReadOnlyList<double> Box
    {
        get => _box;
        init => _box = Copy(value);
    }

    // By value: two manifests read from the same JSON hold equal faces, though each has its own box list.
    public bool Equals(ManifestFace? other) =>
        other is not null
        && Box.SequenceEqual(other.Box)
        && Score.Equals(other.Score)
        && string.Equals(Embedding, other.Embedding, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in Box)
        {
            hash.Add(value);
        }

        hash.Add(Score);
        hash.Add(Embedding, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    /// <summary>
    /// A total order on faces, used to choose between two face lists that are otherwise as good (D-047): score, then
    /// the box value by value, then the embedding text.
    /// </summary>
    public static int Compare(ManifestFace a, ManifestFace b)
    {
        var order = a.Score.CompareTo(b.Score);
        for (var i = 0; order == 0 && i < Math.Min(a.Box.Count, b.Box.Count); i++)
        {
            order = a.Box[i].CompareTo(b.Box[i]);
        }

        order = order != 0 ? order : a.Box.Count.CompareTo(b.Box.Count);
        return order != 0 ? order : string.CompareOrdinal(a.Embedding, b.Embedding);
    }

    private static ReadOnlyCollection<double> Copy(IReadOnlyList<double>? box) =>
        Array.AsReadOnly(box?.ToArray() ?? throw new ArgumentNullException(nameof(box), "A face needs a box."));
}
