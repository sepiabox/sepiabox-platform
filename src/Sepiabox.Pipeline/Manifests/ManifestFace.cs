namespace Sepiabox.Pipeline.Manifests;

/// <summary>A face found in the asset (S-018).</summary>
/// <param name="Box">Left, top, width and height, relative to the image the face was found in (0–1), so the box fits
/// every derivative of the same picture.</param>
/// <param name="Score">The detector's confidence, 0–1.</param>
/// <param name="Embedding">The SFace vector: base64 of 128 float32 little-endian values, L2-normalized.</param>
public sealed record ManifestFace(IReadOnlyList<double> Box, double Score, string Embedding);
