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
    private readonly ReadOnlyCollection<double> _box = Copy(Box);
    private readonly double _score = WithoutNegativeZero(Score);

    /// <summary>A read-only copy, so a face never changes after it is made, even through <c>with</c>.</summary>
    // Box, score, embedding in the JSON, as in the schema, although box and score are declared here.
    [JsonPropertyOrder(-2)]
    public IReadOnlyList<double> Box
    {
        get => _box;
        init => _box = Copy(value);
    }

    /// <summary>The detector's confidence, with -0.0 stored as 0.0.</summary>
    [JsonPropertyOrder(-1)]
    public double Score
    {
        get => _score;
        init => _score = WithoutNegativeZero(value);
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
    /// the box value by value (a shorter box first when one is a prefix of the other), then the embedding text,
    /// ordinal. It agrees with <see cref="Equals(ManifestFace?)"/>: it gives 0 exactly when the faces are
    /// equal, which holds because -0.0 is stored as 0.0 and NaN, which the schema never lets in, equals itself in both.
    /// Null comes first.
    /// </summary>
    public static IComparer<ManifestFace?> Order { get; } = Comparer<ManifestFace?>.Create(Compare);

    private static int Compare(ManifestFace? face, ManifestFace? other)
    {
        if (face is null || other is null)
        {
            return face is null ? (other is null ? 0 : -1) : 1;
        }

        var order = face.Score.CompareTo(other.Score);
        for (var i = 0; order == 0 && i < Math.Min(face.Box.Count, other.Box.Count); i++)
        {
            order = face.Box[i].CompareTo(other.Box[i]);
        }

        order = order != 0 ? order : face.Box.Count.CompareTo(other.Box.Count);
        return order != 0 ? order : string.CompareOrdinal(face.Embedding, other.Embedding);
    }

    private static ReadOnlyCollection<double> Copy(IReadOnlyList<double>? box) =>
        Array.AsReadOnly(box?.Select(WithoutNegativeZero).ToArray() ?? throw new ArgumentNullException(nameof(box), "A face needs a box."));

    // -0.0 equals 0.0 but is written "-0", so it would make two equal faces serialize differently.
    private static double WithoutNegativeZero(double value) => value == 0 ? 0.0 : value;
}
