using System.Text.Json.Nodes;
using ImageMagick;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Faces;
using Sepiabox.Pipeline.Models;
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

    // The reference is OpenCV 4.10's own FaceDetectorYN + FaceRecognizerSF (alignCrop, feature) on the same file,
    // produced by TestData/face-portrait.reference.py.
    [FacesFact]
    public void Matches_opencv_on_the_portrait()
    {
        using var detector = CreateDetector();
        var reference = JsonNode.Parse(File.ReadAllText(Path.Combine("TestData", "face-portrait.reference.json")))!;
        var embedding = EmbeddingCodec.Decode((string)reference["embedding"]!);
        uint width, height;
        using (var image = new MagickImage(s_portrait))
        {
            (width, height) = (image.Width, image.Height);
        }

        var pixels = reference["box"]!.AsArray().Select(v => (double)v!).ToArray();
        double[] box = [pixels[0] / width, pixels[1] / height, pixels[2] / width, pixels[3] / height];

        var face = Assert.Single(detector.Detect(s_portrait));

        var cosine = EmbeddingCodec.Cosine(face.Embedding, embedding);
        output.WriteLine($"cosine with OpenCV {cosine:F4}");
        Assert.True(cosine >= 0.98, $"cosine with OpenCV was {cosine}");
        double[] ours = [face.X, face.Y, face.Width, face.Height];
        for (var i = 0; i < 4; i++)
        {
            Assert.InRange(ours[i], box[i] - 0.02, box[i] + 0.02);
        }
    }

    [FacesFact]
    public void Gives_the_landmarks_in_the_order_of_the_arcface_reference()
    {
        using var detector = CreateDetector();

        var landmarks = Assert.Single(detector.Detect(s_portrait)).Landmarks!;

        // The eye on the left of the picture first, as the reference points: a swap would mirror every aligned face.
        Assert.True(landmarks[0].X < landmarks[1].X, $"eyes at {landmarks[0]} and {landmarks[1]}");
        Assert.True(landmarks[3].X < landmarks[4].X, $"mouth corners at {landmarks[3]} and {landmarks[4]}");
        Assert.True(landmarks[0].Y < landmarks[2].Y && landmarks[2].Y < landmarks[3].Y, "eyes, then nose, then mouth");
    }

    [FacesFact]
    public void Refuses_a_model_without_the_input_the_caller_feeds()
    {
        // A copy of its own, so the verification marker it checks is not the shared models folder's.
        var yunet = ModelCatalog.Get(ModelCatalog.YuNet);
        var copy = _folder.Write(yunet.File, File.ReadAllBytes(ModelFolder.PathOf(TestModels.Folder, yunet)));

        using var named = ModelSession.TryOpen(
            _folder.Path, yunet, "YuNet", "so nothing", "Download it.", [new ModelInput("data")], [], out var nameWarning);
        using var shaped = ModelSession.TryOpen(
            _folder.Path, yunet, "YuNet", "so nothing", "Download it.", [new ModelInput("input", [1, 3, 320, 320])], [], out var shapeWarning);
        using var right = ModelSession.TryOpen(
            _folder.Path, yunet, "YuNet", "so nothing", "Download it.", [new ModelInput("input", [1, 3, 640, 640])], YuNet.OutputNames, out var none);

        Assert.Null(named);
        Assert.Contains("is not the model version this build of sepiabox-ingest expects: it has no input data", nameWarning, StringComparison.Ordinal);
        Assert.Null(shaped);
        Assert.Contains("has no input input [1×3×320×320] (it has input [1×3×640×640])", shapeWarning, StringComparison.Ordinal);
        Assert.NotNull(right);
        Assert.Null(none);
        // The file matches its pin, so its verification is kept: it is the wrong version, not a damaged file.
        Assert.True(File.Exists(ModelVerifier.MarkerPath(copy)));
    }

    private static FaceDetector CreateDetector() =>
        FaceDetector.TryCreate(TestModels.Folder, out var warning) ?? throw new InvalidOperationException(warning);
}
