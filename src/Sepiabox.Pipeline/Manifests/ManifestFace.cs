namespace Sepiabox.Pipeline.Manifests;

/// <summary>A face found in the asset (S-018).</summary>
/// <param name="Box">Left, top, width and height, relative to the upright derivative the face was found in (0–1). A
/// derivative keeps the picture's proportions after orientation, so the box is mapped with a derivative's width and
/// height, never the manifest's, which are the original's as stored.</param>
/// <param name="Score">The detector's confidence, 0–1.</param>
/// <param name="Embedding">The SFace vector: base64 of 128 float32 little-endian values, L2-normalized.</param>
public sealed record ManifestFace(IReadOnlyList<double> Box, double Score, string Embedding)
{
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
}
