namespace Sepiabox.Pipeline.Faces;

/// <summary>A face found by YuNet, in the pixels of the image it ran on.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width of the box.</param>
/// <param name="Height">Height of the box.</param>
/// <param name="Score">Confidence, 0–1.</param>
/// <param name="Landmarks">Eyes, nose tip and mouth corners, in <see cref="FaceAlignment.ArcFaceReference"/>'s order.</param>
public sealed record FaceCandidate(double X, double Y, double Width, double Height, double Score, IReadOnlyList<FacePoint> Landmarks)
{
    public FaceCandidate Scaled(double scaleX, double scaleY) => this with
    {
        X = X * scaleX,
        Y = Y * scaleY,
        Width = Width * scaleX,
        Height = Height * scaleY,
        Landmarks = [.. Landmarks.Select(p => new FacePoint(p.X * scaleX, p.Y * scaleY))],
    };
}

/// <summary>
/// Reads the outputs of YuNet (<c>face_detection_yunet_2023mar</c>) as OpenCV's <c>FaceDetectorYN</c> does: one
/// prediction per cell of three grids (strides 8, 16 and 32), score √(class × objectness), box and landmarks
/// relative to the cell, then non-maximum suppression.
/// </summary>
public static class YuNet
{
    /// <summary>The model's fixed input: 640×640 BGR, values 0–255, channel first.</summary>
    public const int InputSize = 640;

    public static readonly IReadOnlyList<int> Strides = [8, 16, 32];

    /// <summary>OpenCV's default: boxes that overlap more than this with a better one are the same face.</summary>
    public const double NmsThreshold = 0.3;

    /// <summary>The outputs of one stride: <c>cls_8</c>, <c>obj_8</c>, <c>bbox_8</c> (4 per cell) and <c>kps_8</c> (10 per cell).</summary>
    public sealed record StrideOutput(int Stride, float[] Class, float[] Objectness, float[] Box, float[] Landmarks);

    public static readonly IReadOnlyList<string> OutputNames =
        [.. Strides.SelectMany(stride => new[] { $"cls_{stride}", $"obj_{stride}", $"bbox_{stride}", $"kps_{stride}" })];

    /// <summary>Faces with a score of at least <paramref name="minimumScore"/>, best first, in input pixels.</summary>
    public static List<FaceCandidate> Decode(IReadOnlyList<StrideOutput> outputs, double minimumScore)
    {
        var candidates = new List<FaceCandidate>();
        foreach (var output in outputs)
        {
            var columns = InputSize / output.Stride;
            var cells = columns * columns;
            if (output.Class.Length != cells || output.Objectness.Length != cells
                || output.Box.Length != cells * 4 || output.Landmarks.Length != cells * 10)
            {
                throw new ArgumentException($"The outputs for stride {output.Stride} do not have {cells} cells.", nameof(outputs));
            }

            for (var cell = 0; cell < cells; cell++)
            {
                var score = Math.Sqrt(Math.Clamp(output.Class[cell], 0f, 1f) * Math.Clamp(output.Objectness[cell], 0f, 1f));
                // Written so that a NaN score, which fails every comparison, is left out too.
                if (!(score >= minimumScore))
                {
                    continue;
                }

                var row = cell / columns;
                var column = cell % columns;
                var stride = output.Stride;
                var centreX = (column + output.Box[cell * 4]) * stride;
                var centreY = (row + output.Box[(cell * 4) + 1]) * stride;
                var width = Math.Exp(output.Box[(cell * 4) + 2]) * stride;
                var height = Math.Exp(output.Box[(cell * 4) + 3]) * stride;
                var landmarks = new FacePoint[5];
                for (var n = 0; n < landmarks.Length; n++)
                {
                    landmarks[n] = new FacePoint(
                        (output.Landmarks[(cell * 10) + (2 * n)] + column) * stride,
                        (output.Landmarks[(cell * 10) + (2 * n) + 1] + row) * stride);
                }

                // A cell with a box or landmark that is not finite has nothing to crop or compare.
                if (!double.IsFinite(centreX) || !double.IsFinite(centreY) || !double.IsFinite(width) || !double.IsFinite(height)
                    || landmarks.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
                {
                    continue;
                }

                candidates.Add(new FaceCandidate(centreX - (width / 2), centreY - (height / 2), width, height, score, landmarks));
            }
        }

        return Suppress(candidates, NmsThreshold);
    }

    /// <summary>Keeps the best of each group of boxes that overlap more than <paramref name="threshold"/> (intersection over union).</summary>
    public static List<FaceCandidate> Suppress(IEnumerable<FaceCandidate> candidates, double threshold)
    {
        var kept = new List<FaceCandidate>();
        foreach (var candidate in candidates.OrderByDescending(c => c.Score))
        {
            if (kept.All(better => IntersectionOverUnion(better, candidate) <= threshold))
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    public static double IntersectionOverUnion(FaceCandidate a, FaceCandidate b)
    {
        var width = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
        var height = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        var intersection = width * height;
        return intersection / ((a.Width * a.Height) + (b.Width * b.Height) - intersection);
    }
}
