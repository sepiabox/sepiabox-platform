using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Faces;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public sealed class ManifestFacesTests : IDisposable
{
    private readonly TempFolder _work = new();

    public void Dispose() => _work.Dispose();

    [Fact]
    public void Adds_the_faces_of_a_photo_from_its_1280_preview()
    {
        var detector = new FakeFaceDetector();

        var outcome = ManifestFaces.TryAdd(WithDerivativeFiles(Photo()), _work.Path, detector);

        Assert.Equal(EmbeddingStatus.Added, outcome.Status);
        Assert.EndsWith("preview-1280.webp", Assert.Single(detector.Detected), StringComparison.Ordinal);
        Assert.True(outcome.Manifest!.FacesProcessed);
        Assert.Equal(ManifestFaces.CurrentVersion, outcome.Manifest.FacesVersion);
        var face = Assert.Single(outcome.Manifest.Faces);
        Assert.Equal([0.25, 0.2, 0.5, 0.6], face.Box);
        Assert.Equal(0.97, face.Score);
        Assert.Equal(FaceDetector.Dimensions, EmbeddingCodec.Decode(face.Embedding).Length);
    }

    [Fact]
    public void Marks_faces_processed_when_the_photo_has_none()
    {
        var outcome = ManifestFaces.TryAdd(WithDerivativeFiles(Photo()), _work.Path, new FakeFaceDetector { Faces = [] });

        Assert.Equal(EmbeddingStatus.Added, outcome.Status);
        Assert.True(outcome.Manifest!.FacesProcessed);
        Assert.Empty(outcome.Manifest.Faces);
    }

    [Fact]
    public void Adds_the_faces_of_a_video_from_its_poster()
    {
        var video = Photo() with
        {
            Kind = MediaKind.Video,
            Derivatives = [TestManifests.Derivative("poster", 1280, 720), TestManifests.Derivative("proxy-720p", 1280, 720)],
        };
        var detector = new FakeFaceDetector();

        var outcome = ManifestFaces.TryAdd(WithDerivativeFiles(video), _work.Path, detector);

        Assert.Equal(EmbeddingStatus.Added, outcome.Status);
        Assert.EndsWith("poster.webp", Assert.Single(detector.Detected), StringComparison.Ordinal);
    }

    [Fact]
    public void Leaves_a_manifest_whose_faces_were_processed_alone()
    {
        var manifest = WithDerivativeFiles(Photo()) with { FacesProcessed = true };
        var detector = new FakeFaceDetector();

        var outcome = ManifestFaces.TryAdd(manifest, _work.Path, detector);

        Assert.Equal(EmbeddingStatus.Present, outcome.Status);
        Assert.Same(manifest, outcome.Manifest);
        Assert.Empty(detector.Detected);
    }

    [Fact]
    public void Looks_again_for_faces_found_by_an_older_face_pipeline_and_replaces_them()
    {
        // Version 0 stands for any version older than the current one; the schema never stores it.
        var outdated = WithDerivativeFiles(Photo()) with
        {
            FacesProcessed = true,
            FacesVersion = ManifestFaces.CurrentVersion - 1,
            Faces = [TestManifests.Face(0.5, axis: 7), TestManifests.Face(0.4, axis: 8)],
        };
        var detector = new FakeFaceDetector();

        var outcome = ManifestFaces.TryAdd(outdated, _work.Path, detector);

        Assert.Equal(EmbeddingStatus.Added, outcome.Status);
        Assert.Single(detector.Detected);
        Assert.Equal(ManifestFaces.CurrentVersion, outcome.Manifest!.FacesVersion);
        Assert.Equal(0.97, Assert.Single(outcome.Manifest.Faces).Score);
    }

    [Fact]
    public void Leaves_faces_from_a_newer_face_pipeline_alone()
    {
        var newer = WithDerivativeFiles(Photo()) with { FacesProcessed = true, FacesVersion = ManifestFaces.CurrentVersion + 1 };
        var detector = new FakeFaceDetector();

        Assert.Equal(EmbeddingStatus.Present, ManifestFaces.TryAdd(newer, _work.Path, detector).Status);
        Assert.Empty(detector.Detected);
    }

    [Fact]
    public void Reports_no_derivative_when_the_preview_is_not_in_the_work_folder()
    {
        var outcome = ManifestFaces.TryAdd(Photo(), _work.Path, new FakeFaceDetector());

        Assert.Equal(EmbeddingStatus.NoDerivative, outcome.Status);
        Assert.False(outcome.Manifest!.FacesProcessed);
    }

    [Fact]
    public void Reports_not_listed_when_the_manifest_lists_no_preview()
    {
        var manifest = Photo() with { Derivatives = [TestManifests.Derivative("thumb-256", 256, 192)] };

        var outcome = ManifestFaces.TryAdd(WithDerivativeFiles(manifest), _work.Path, new FakeFaceDetector());

        Assert.Equal(EmbeddingStatus.NotListed, outcome.Status);
    }

    [Fact]
    public void Reports_a_failure_without_throwing_and_leaves_faces_unprocessed()
    {
        var detector = new FakeFaceDetector { Failure = _ => new InvalidOperationException("The model broke.") };

        var outcome = ManifestFaces.TryAdd(WithDerivativeFiles(Photo()), _work.Path, detector);

        Assert.Equal(EmbeddingStatus.Failed, outcome.Status);
        Assert.False(outcome.Manifest!.FacesProcessed);
        Assert.Equal("Could not detect faces (InvalidOperationException)", outcome.Category);
        Assert.Contains("The model broke.", outcome.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Lets_cancellation_through()
    {
        var detector = new FakeFaceDetector { Failure = _ => new OperationCanceledException() };

        Assert.Throws<OperationCanceledException>(() => ManifestFaces.TryAdd(WithDerivativeFiles(Photo()), _work.Path, detector));
    }

    [Fact]
    public void Fails_a_face_whose_embedding_has_the_wrong_length()
    {
        var detector = new FakeFaceDetector { Faces = [FakeFaceDetector.CentreFace() with { Embedding = new float[127] }] };

        Assert.Equal(EmbeddingStatus.Failed, ManifestFaces.TryAdd(WithDerivativeFiles(Photo()), _work.Path, detector).Status);
    }

    [Fact]
    public void Fails_a_face_whose_box_is_not_finite()
    {
        var detector = new FakeFaceDetector { Faces = [FakeFaceDetector.CentreFace() with { Width = double.NaN }] };

        Assert.Equal(EmbeddingStatus.Failed, ManifestFaces.TryAdd(WithDerivativeFiles(Photo()), _work.Path, detector).Status);
    }

    [Fact]
    public void Rounds_boxes_and_scores_to_four_decimals_inside_zero_and_one_and_normalizes_the_embedding()
    {
        var embedding = new float[FaceDetector.Dimensions];
        embedding[0] = 3f;
        embedding[1] = 4f;

        var face = ManifestFaces.ToManifest(new DetectedFace(-0.00001, 0.123456, 0.5, 1.2, 0.987654, embedding));

        Assert.Equal([0, 0.1235, 0.5, 0.8765], face.Box);
        Assert.Equal(0.9877, face.Score);
        var stored = EmbeddingCodec.Decode(face.Embedding);
        Assert.Equal(0.6f, stored[0], 6);
        Assert.Equal(0.8f, stored[1], 6);
    }

    [Fact]
    public void Keeps_a_box_inside_the_image_after_rounding()
    {
        var face = ManifestFaces.ToManifest(FakeFaceDetector.CentreFace() with { X = 0.33335, Width = 0.66666, Y = 0.99996, Height = 0.00004 });

        // Within a rounding error of a double, never by a whole ten-thousandth.
        Assert.True(face.Box[0] + face.Box[2] <= 1 + 1e-9, string.Join(", ", face.Box));
        Assert.True(face.Box[1] + face.Box[3] <= 1 + 1e-9, string.Join(", ", face.Box));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(0f)]
    public void Leaves_out_a_face_whose_embedding_has_no_direction(float value)
    {
        var broken = FakeFaceDetector.CentreFace() with { Embedding = Enumerable.Repeat(value, FaceDetector.Dimensions).ToArray() };
        var detector = new FakeFaceDetector { Faces = [broken, FakeFaceDetector.CentreFace(0.9)] };

        var outcome = ManifestFaces.TryAdd(WithDerivativeFiles(Photo()), _work.Path, detector);

        Assert.Equal(EmbeddingStatus.Added, outcome.Status);
        Assert.Equal(0.9, Assert.Single(outcome.Manifest!.Faces).Score);
    }

    private static Manifest Photo() => TestManifests.Photo(TestManifests.Source("HD da Ana", "2019/praia.jpg"));

    private Manifest WithDerivativeFiles(Manifest manifest)
    {
        foreach (var derivative in manifest.Derivatives)
        {
            _work.Write(derivative.Path, [1, 2, 3]);
        }

        return manifest;
    }
}
