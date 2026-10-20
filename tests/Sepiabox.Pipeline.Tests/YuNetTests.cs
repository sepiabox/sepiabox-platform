using Sepiabox.Pipeline.Faces;

namespace Sepiabox.Pipeline.Tests;

public sealed class YuNetTests
{
    [Fact]
    public void Decodes_a_cell_into_a_box_and_landmarks_in_input_pixels()
    {
        var outputs = Empty();
        // Stride 16, row 10, column 12: the cell's corner is at (192, 160).
        var cell = (10 * (640 / 16)) + 12;
        var stride16 = outputs[1];
        stride16.Class[cell] = 0.81f;
        stride16.Objectness[cell] = 1f;
        stride16.Box[cell * 4] = 0.5f;
        stride16.Box[(cell * 4) + 1] = 0.25f;
        stride16.Box[(cell * 4) + 2] = (float)Math.Log(4);
        stride16.Box[(cell * 4) + 3] = (float)Math.Log(5);
        for (var n = 0; n < 5; n++)
        {
            stride16.Landmarks[(cell * 10) + (2 * n)] = n * 0.1f;
            stride16.Landmarks[(cell * 10) + (2 * n) + 1] = 0.5f;
        }

        var face = Assert.Single(YuNet.Decode(outputs, 0.8));

        Assert.Equal(0.9, face.Score, 6);
        // Centre (12.5 × 16, 10.25 × 16) = (200, 164), size 64 × 80.
        Assert.Equal((168.0, 124.0, 64.0, 80.0), (Math.Round(face.X, 3), Math.Round(face.Y, 3), Math.Round(face.Width, 3), Math.Round(face.Height, 3)));
        Assert.Equal(new FacePoint(192, 168), face.Landmarks[0]);
        Assert.Equal(192 + (0.4 * 16), face.Landmarks[4].X, 4);
    }

    [Fact]
    public void Leaves_out_cells_below_the_minimum_score()
    {
        var outputs = Empty();
        outputs[0].Class[5] = 0.79f * 0.79f;
        outputs[0].Objectness[5] = 1f;

        Assert.Empty(YuNet.Decode(outputs, 0.8));
    }

    [Fact]
    public void Clamps_class_and_objectness_to_zero_and_one()
    {
        var outputs = Empty();
        outputs[2].Class[3] = 1.4f;
        outputs[2].Objectness[3] = 2f;

        Assert.Equal(1, Assert.Single(YuNet.Decode(outputs, 0.8)).Score);
    }

    [Fact]
    public void Keeps_only_the_best_of_overlapping_boxes()
    {
        var best = Candidate(100, 100, 50, 0.95);
        var overlapping = Candidate(105, 102, 50, 0.9);
        var apart = Candidate(300, 100, 50, 0.85);

        var kept = YuNet.Suppress([overlapping, apart, best], YuNet.NmsThreshold);

        Assert.Equal([best, apart], kept);
    }

    [Fact]
    public void Computes_intersection_over_union()
    {
        Assert.Equal(1.0 / 3, YuNet.IntersectionOverUnion(Candidate(0, 0, 10, 1), Candidate(5, 0, 10, 1) with { Height = 10 }), 6);
        Assert.Equal(0, YuNet.IntersectionOverUnion(Candidate(0, 0, 10, 1), Candidate(20, 20, 10, 1)));
    }

    [Fact]
    public void Refuses_outputs_of_the_wrong_size()
    {
        var outputs = Empty();
        outputs[0] = outputs[0] with { Class = new float[3] };

        Assert.Throws<ArgumentException>(() => YuNet.Decode(outputs, 0.8));
    }

    [Fact]
    public void Scales_a_face_back_to_the_original_image()
    {
        var face = Candidate(10, 20, 30, 0.9).Scaled(2, 3);

        Assert.Equal((20.0, 60.0, 60.0, 90.0), (face.X, face.Y, face.Width, face.Height));
        Assert.Equal(new FacePoint(20, 60), face.Landmarks[0]);
    }

    private static FaceCandidate Candidate(double x, double y, double side, double score) =>
        new(x, y, side, side, score, [.. Enumerable.Repeat(new FacePoint(x, y), 5)]);

    private static List<YuNet.StrideOutput> Empty() =>
    [
        .. YuNet.Strides.Select(stride =>
        {
            var cells = (640 / stride) * (640 / stride);
            return new YuNet.StrideOutput(stride, new float[cells], new float[cells], new float[cells * 4], new float[cells * 10]);
        }),
    ];
}
