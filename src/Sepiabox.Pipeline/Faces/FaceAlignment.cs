namespace Sepiabox.Pipeline.Faces;

/// <summary>
/// Cuts a face out of an image as SFace expects it: 112×112, with the five landmarks moved onto the ArcFace
/// reference points by a similarity transform (S-018), as OpenCV's <c>FaceRecognizerSF::alignCrop</c> does.
/// </summary>
public static class FaceAlignment
{
    public const int Size = 112;

    /// <summary>
    /// Where the eyes, nose tip and mouth corners go in the 112×112 crop, in YuNet's landmark order: the eye on the
    /// left of the picture, the other eye, the nose tip, the mouth corner on the left, the other mouth corner.
    /// </summary>
    public static readonly IReadOnlyList<FacePoint> ArcFaceReference =
    [
        new(38.2946, 51.6963),
        new(73.5318, 51.5014),
        new(56.0252, 71.7366),
        new(41.5493, 92.3655),
        new(70.7299, 92.2041),
    ];

    /// <summary>The transform that moves <paramref name="landmarks"/> (five points, in image pixels) onto the reference.</summary>
    public static SimilarityTransform TransformFor(IReadOnlyList<FacePoint> landmarks)
    {
        if (landmarks.Count != ArcFaceReference.Count)
        {
            throw new ArgumentException($"Expected {ArcFaceReference.Count} landmarks, got {landmarks.Count}.", nameof(landmarks));
        }

        return SimilarityTransform.Estimate(landmarks, ArcFaceReference);
    }

    /// <summary>
    /// The aligned 112×112 crop of interleaved RGB <paramref name="rgb"/> (<paramref name="width"/> ×
    /// <paramref name="height"/>), as interleaved RGB bytes. Each crop pixel is read from the image through the
    /// inverse transform with bilinear interpolation; outside the image is black, like OpenCV's constant border.
    /// </summary>
    public static byte[] Crop(ReadOnlySpan<byte> rgb, int width, int height, SimilarityTransform transform)
    {
        if (rgb.Length != width * height * 3)
        {
            throw new ArgumentException($"Expected {width * height * 3} bytes of {width}×{height} RGB, got {rgb.Length}.", nameof(rgb));
        }

        var inverse = transform.Inverse();
        var crop = new byte[Size * Size * 3];
        Span<double> pixel = stackalloc double[3];
        for (var v = 0; v < Size; v++)
        {
            for (var u = 0; u < Size; u++)
            {
                var source = inverse.Apply(new FacePoint(u, v));
                var x0 = (int)Math.Floor(source.X);
                var y0 = (int)Math.Floor(source.Y);
                var fx = source.X - x0;
                var fy = source.Y - y0;
                pixel.Clear();
                Accumulate(rgb, width, height, x0, y0, (1 - fx) * (1 - fy), pixel);
                Accumulate(rgb, width, height, x0 + 1, y0, fx * (1 - fy), pixel);
                Accumulate(rgb, width, height, x0, y0 + 1, (1 - fx) * fy, pixel);
                Accumulate(rgb, width, height, x0 + 1, y0 + 1, fx * fy, pixel);
                var offset = ((v * Size) + u) * 3;
                for (var channel = 0; channel < 3; channel++)
                {
                    crop[offset + channel] = (byte)Math.Clamp(Math.Round(pixel[channel]), 0, 255);
                }
            }
        }

        return crop;
    }

    private static void Accumulate(ReadOnlySpan<byte> rgb, int width, int height, int x, int y, double weight, Span<double> pixel)
    {
        if (weight == 0 || x < 0 || y < 0 || x >= width || y >= height)
        {
            return;
        }

        var offset = ((y * width) + x) * 3;
        for (var channel = 0; channel < 3; channel++)
        {
            pixel[channel] += rgb[offset + channel] * weight;
        }
    }
}
