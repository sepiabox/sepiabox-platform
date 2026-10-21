using Sepiabox.Catalog.People;

namespace Sepiabox.Worker.Tests.People;

/// <summary>Grouping faces with synthetic vectors (S-019): no real face is ever used, faces being biometric data.</summary>
public sealed class FaceAssignerTests
{
    [Fact]
    public void Chooses_the_same_person_when_two_faces_have_nearly_identical_embeddings()
    {
        var face = SyntheticFaces.Unit(seed: 1);
        var again = SyntheticFaces.Near(face, seed: 2);

        Assert.Equal(0, FaceAssigner.Choose([face], again, FaceAssignerOptions.DefaultThreshold));
    }

    [Fact]
    public void Starts_a_new_person_when_the_faces_are_distant()
    {
        var face = SyntheticFaces.Unit(seed: 1);
        var other = SyntheticFaces.Unit(seed: 3);

        Assert.True(FaceMath.Cosine(face, other) < FaceAssignerOptions.DefaultThreshold);
        Assert.Null(FaceAssigner.Choose([face], other, FaceAssignerOptions.DefaultThreshold));
    }

    [Fact]
    public void Chooses_the_most_similar_person_when_several_pass_the_threshold()
    {
        var face = SyntheticFaces.Unit(seed: 1);
        var close = SyntheticFaces.Near(face, seed: 4, noise: 0.05);
        var closer = SyntheticFaces.Near(face, seed: 5, noise: 0.01);

        Assert.Equal(2, FaceAssigner.Choose([SyntheticFaces.Unit(seed: 9), close, closer], face, FaceAssignerOptions.DefaultThreshold));
    }

    [Fact]
    public void Joins_a_person_from_a_cosine_of_exactly_the_threshold()
    {
        var person = SyntheticFaces.Unit(seed: 1);
        var face = SyntheticFaces.Near(person, seed: 6, noise: 0.5);
        var cosine = FaceMath.Cosine(person, face);

        Assert.Equal(0, FaceAssigner.Choose([person], face, cosine));
        Assert.Null(FaceAssigner.Choose([person], face, Math.BitIncrement(cosine)));
    }

    [Fact]
    public void Centroid_is_the_normalized_mean_of_the_faces()
    {
        var a = new float[128];
        a[0] = 1;
        var b = new float[128];
        b[1] = 1;

        var centroid = FaceMath.Centroid([a, b])!;

        Assert.Equal(1 / Math.Sqrt(2), centroid[0], 6);
        Assert.Equal(1 / Math.Sqrt(2), centroid[1], 6);
        Assert.Null(FaceMath.Centroid([]));
    }

    [Fact]
    public void Matches_a_slightly_moved_box_and_not_a_distant_one()
    {
        double[] stored = [0.10, 0.20, 0.30, 0.40];
        double[] moved = [0.12, 0.21, 0.30, 0.40];
        double[] elsewhere = [0.60, 0.20, 0.30, 0.40];

        Assert.True(FaceMath.IoU(stored, moved) >= FaceAssigner.SameBoxIoU);
        Assert.Equal(0, FaceMath.IoU(stored, elsewhere));
        Assert.Equal(new Dictionary<int, int> { [1] = 0 }, FaceAssigner.Match([stored], [elsewhere, moved]));
    }

    [Fact]
    public void Matches_each_stored_box_once_with_the_largest_overlap_first()
    {
        double[] stored = [0.10, 0.10, 0.20, 0.20];
        double[] exact = [0.10, 0.10, 0.20, 0.20];
        double[] near = [0.11, 0.11, 0.20, 0.20];

        Assert.Equal(new Dictionary<int, int> { [1] = 0 }, FaceAssigner.Match([stored], [near, exact]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void Refuses_a_threshold_outside_zero_to_one(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FaceAssignerOptions.For(threshold));
    }
}

/// <summary>Random 128-value unit vectors standing in for SFace embeddings.</summary>
internal static class SyntheticFaces
{
    public static float[] Unit(int seed)
    {
        var random = new Random(seed);
        return Normalized(Enumerable.Range(0, 128).Select(_ => random.NextDouble() - 0.5).ToArray());
    }

    /// <summary>The same face with a little noise: a cosine well above 0.9 with <paramref name="face"/>.</summary>
    public static float[] Near(float[] face, int seed, double noise = 0.02)
    {
        var random = new Random(seed);
        return Normalized(face.Select(v => v + ((random.NextDouble() - 0.5) * noise)).ToArray());
    }

    private static float[] Normalized(double[] values)
    {
        var length = Math.Sqrt(values.Sum(v => v * v));
        return [.. values.Select(v => (float)(v / length))];
    }
}
