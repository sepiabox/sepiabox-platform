using ImageMagick;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Faces;
using Xunit.Abstractions;

namespace Sepiabox.Pipeline.Tests;

/// <summary>YuNet and SFace on real pictures (S-018); see <c>TestData/README.md</c> for the portrait.</summary>
[Trait("Category", "Integration")]
public sealed class FaceDetectorTests(ITestOutputHelper output) : IDisposable
{
    private static readonly string s_portrait = Path.Combine("TestData", "face-portrait.jpg");

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [FacesFact]
    public void Finds_no_face_in_a_solid_colour_image()
    {
        using var detector = CreateDetector();
        var path = Path.Combine(_folder.Path, "flat.webp");
        using (var image = new MagickImage(MagickColors.Tan, 1280, 960))
        {
            image.Write(path);
        }

        Assert.Empty(detector.Detect(path));
    }

    [FacesFact]
    public void Finds_the_one_face_of_the_portrait_with_a_score_of_at_least_0_8_and_a_box_inside_the_image()
    {
        using var detector = CreateDetector();

        var face = Assert.Single(detector.Detect(s_portrait));

        output.WriteLine($"score {face.Score:F4}, box {face.X:F4} {face.Y:F4} {face.Width:F4} {face.Height:F4}");
        Assert.InRange(face.Score, FaceDetector.MinimumScore, 1);
        Assert.InRange(face.X, 0, 1);
        Assert.InRange(face.Y, 0, 1);
        Assert.InRange(face.Width, 0.01, 1 - face.X);
        Assert.InRange(face.Height, 0.01, 1 - face.Y);
        // The face is in the upper middle of the portrait, as a person looking at it would draw the box.
        Assert.InRange(face.X + (face.Width / 2), 0.3, 0.5);
        Assert.InRange(face.Y + (face.Height / 2), 0.1, 0.4);
    }

    [FacesFact]
    public void Gives_a_128_value_embedding_of_length_one()
    {
        using var detector = CreateDetector();

        var embedding = Assert.Single(detector.Detect(s_portrait)).Embedding;

        Assert.Equal(FaceDetector.Dimensions, embedding.Length);
        Assert.InRange(Math.Sqrt(embedding.Sum(v => (double)v * v)), 0.999, 1.001);
    }

    [FacesFact]
    public void The_portrait_resized_to_70_percent_gives_an_embedding_with_a_cosine_of_at_least_0_6()
    {
        using var detector = CreateDetector();
        var smaller = Path.Combine(_folder.Path, "portrait-70.jpg");
        using (var image = new MagickImage(s_portrait))
        {
            image.Resize(new Percentage(70));
            image.Write(smaller);
        }

        var original = Assert.Single(detector.Detect(s_portrait)).Embedding;
        var resized = Assert.Single(detector.Detect(smaller)).Embedding;

        var cosine = EmbeddingCodec.Cosine(original, resized);
        output.WriteLine($"cosine {cosine:F4}");
        Assert.True(cosine >= 0.6, $"cosine was {cosine}");
    }

    [FacesFact]
    public void Leaves_out_faces_smaller_than_40_pixels()
    {
        using var detector = CreateDetector();
        // The face is about 70 pixels wide in the 400-pixel portrait: at a quarter it is under 20.
        var tiny = Path.Combine(_folder.Path, "portrait-25.jpg");
        using (var image = new MagickImage(s_portrait))
        {
            image.Resize(new Percentage(25));
            image.Write(tiny);
        }

        Assert.Empty(detector.Detect(tiny));
    }

    private static FaceDetector CreateDetector() =>
        FaceDetector.TryCreate(TestModels.Folder, out var warning) ?? throw new InvalidOperationException(warning);
}
