namespace Sepiabox.Pipeline.Faces;

/// <summary>A point in pixel coordinates, where (0, 0) is the centre of the top-left pixel.</summary>
public readonly record struct FacePoint(double X, double Y);

/// <summary>
/// Rotation, uniform scale and translation, without shear or reflection: x' = A·x − B·y + Tx, y' = B·x + A·y + Ty.
/// It moves a face onto the reference points without distorting it.
/// </summary>
public readonly record struct SimilarityTransform(double A, double B, double Tx, double Ty)
{
    public static SimilarityTransform Identity => new(1, 0, 0, 0);

    public double Scale => Math.Sqrt((A * A) + (B * B));

    public FacePoint Apply(FacePoint point) =>
        new((A * point.X) - (B * point.Y) + Tx, (B * point.X) + (A * point.Y) + Ty);

    public SimilarityTransform Inverse()
    {
        var determinant = (A * A) + (B * B);
        if (determinant == 0)
        {
            throw new InvalidOperationException("A transform that collapses every point into one cannot be inverted.");
        }

        var a = A / determinant;
        var b = -B / determinant;
        return new SimilarityTransform(a, b, -((a * Tx) - (b * Ty)), -((b * Tx) + (a * Ty)));
    }

    /// <summary>
    /// The least-squares similarity that moves <paramref name="from"/> onto <paramref name="to"/>, point by point:
    /// the closed form of Umeyama's estimate without reflection, the same as OpenCV's SFace alignment.
    /// </summary>
    public static SimilarityTransform Estimate(IReadOnlyList<FacePoint> from, IReadOnlyList<FacePoint> to)
    {
        if (from.Count != to.Count || from.Count < 2)
        {
            throw new ArgumentException("A similarity needs at least two pairs of points.", nameof(to));
        }

        var fromMean = Mean(from);
        var toMean = Mean(to);
        double squares = 0, dot = 0, cross = 0;
        for (var i = 0; i < from.Count; i++)
        {
            var (fx, fy) = (from[i].X - fromMean.X, from[i].Y - fromMean.Y);
            var (tx, ty) = (to[i].X - toMean.X, to[i].Y - toMean.Y);
            squares += (fx * fx) + (fy * fy);
            dot += (fx * tx) + (fy * ty);
            cross += (fx * ty) - (fy * tx);
        }

        if (squares == 0)
        {
            throw new ArgumentException("The points to move are all the same point.", nameof(from));
        }

        var a = dot / squares;
        var b = cross / squares;
        var moved = new SimilarityTransform(a, b, 0, 0).Apply(fromMean);
        return new SimilarityTransform(a, b, toMean.X - moved.X, toMean.Y - moved.Y);
    }

    private static FacePoint Mean(IReadOnlyList<FacePoint> points) =>
        new(points.Average(p => p.X), points.Average(p => p.Y));
}
