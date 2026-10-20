using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Faces;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Models;

namespace Sepiabox.Ingest.Tests;

/// <summary>How process handles faces (S-018), with a fake detector instead of YuNet and SFace.</summary>
public sealed class ProcessFaceTests : IDisposable
{
    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [Fact]
    public void Without_faces_no_face_is_looked_for_and_faces_processed_is_false()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());
        var detector = new FakeFaceDetector();

        var result = Run(detector, faces: false);

        Assert.Empty(detector.Detected);
        Assert.Equal((0, 0), (result.FacesChecked, result.FacesFound));
        var manifest = Assert.Single(ReadManifests());
        Assert.False(manifest.FacesProcessed);
        Assert.Empty(manifest.Faces);
    }

    [Fact]
    public void With_faces_stores_each_face_found_in_the_preview()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());
        var detector = new FakeFaceDetector();

        var result = Run(detector);

        Assert.EndsWith("preview-1280.webp", Assert.Single(detector.Detected), StringComparison.Ordinal);
        Assert.Equal((1, 1, 1), (result.Processed, result.FacesChecked, result.FacesFound));
        var manifest = Assert.Single(ReadManifests());
        Assert.True(manifest.FacesProcessed);
        var face = Assert.Single(manifest.Faces);
        Assert.Equal([0.25, 0.2, 0.5, 0.6], face.Box);
        Assert.Equal(FaceDetector.Dimensions, EmbeddingCodec.Decode(face.Embedding).Length);
    }

    [Fact]
    public void A_later_run_with_faces_adds_them_from_the_work_folder_without_describing_the_file_again()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());
        Run(new FakeFaceDetector(), faces: false);

        var result = Run(new FakeFaceDetector());

        Assert.Equal((0, 1, 1), (result.Processed, result.Skipped, result.FacesChecked));
        Assert.True(Assert.Single(ReadManifests()).FacesProcessed);
        Assert.Equal(0, Run(new FakeFaceDetector()).FacesChecked);
    }

    [Fact]
    public void A_later_run_without_faces_keeps_the_faces_already_found()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());
        Run(new FakeFaceDetector());

        Run(new FakeFaceDetector(), faces: false);

        var manifest = Assert.Single(ReadManifests());
        Assert.True(manifest.FacesProcessed);
        Assert.Single(manifest.Faces);
    }

    [Fact]
    public void A_failed_detection_is_a_warning_not_a_failure_and_the_next_run_tries_again()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());
        var failing = new FakeFaceDetector { Failure = _ => new EmbeddingException("The SFace model returned 3 values instead of 128.") };

        var result = Run(failing);

        Assert.Equal(1, result.Processed);
        Assert.Empty(result.Problems);
        var problem = Assert.Single(result.FaceProblems);
        Assert.Equal("Could not detect faces (EmbeddingException)", problem.Category);
        Assert.False(Assert.Single(ReadManifests()).FacesProcessed);

        var retry = Run(new FakeFaceDetector());

        Assert.Equal((1, 1), (retry.Skipped, retry.FacesChecked));
        Assert.Empty(retry.FaceProblems);
    }

    [Fact]
    public void Faces_and_embeddings_are_added_in_the_same_run()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());

        var result = ProcessCommand.Run(Options(faces: true), new FakeEmbedder(), null, faces: new FakeFaceDetector());

        Assert.Equal((1, 1), (result.Embedded, result.FacesChecked));
        var manifest = Assert.Single(ReadManifests());
        Assert.True(manifest.Embeddings.ContainsKey(ClipImageEmbedder.ModelKey));
        Assert.True(manifest.FacesProcessed);
    }

    [Fact]
    public void Missing_face_models_with_faces_give_a_warning_and_the_run_goes_on()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());
        using var models = new TempFolder();

        var result = ProcessCommand.Run(Options(faces: true) with { ModelFolder = models.Path });

        Assert.Equal(1, result.Processed);
        Assert.Contains("so no faces were detected", result.Warning, StringComparison.Ordinal);
        Assert.False(Assert.Single(ReadManifests()).FacesProcessed);
    }

    [Fact]
    public void Missing_face_models_without_faces_give_no_face_warning()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());
        using var models = new TempFolder();

        var result = ProcessCommand.Run(Options(faces: false) with { ModelFolder = models.Path });

        Assert.DoesNotContain("faces", result.Warning ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Previews_missing_from_the_work_folder_are_one_warning_for_faces_too()
    {
        _drive.Write("2019/a.jpg", TestPhotos.Porto());
        Run(new FakeFaceDetector(), faces: false);
        Directory.Delete(Path.Combine(_work.Path, "derivatives"), recursive: true);

        var result = Run(new FakeFaceDetector());

        Assert.Equal(0, result.FacesChecked);
        Assert.Contains("1 photos and videos have no preview or poster in the work folder, so they were not checked for faces", result.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_previews_name_both_embeddings_and_faces_when_both_are_missing()
    {
        _drive.Write("2019/a.jpg", TestPhotos.Porto());
        Run(new FakeFaceDetector(), faces: false);
        Directory.Delete(Path.Combine(_work.Path, "derivatives"), recursive: true);

        var result = ProcessCommand.Run(Options(faces: true), new FakeEmbedder(), null, faces: new FakeFaceDetector());

        Assert.Contains("1 photos and videos have no preview or poster in the work folder, so they have no embedding or were not checked for faces", result.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_previews_name_only_the_embedding_when_the_faces_were_already_found()
    {
        _drive.Write("2019/a.jpg", TestPhotos.Porto());
        Run(new FakeFaceDetector());
        Directory.Delete(Path.Combine(_work.Path, "derivatives"), recursive: true);

        var result = ProcessCommand.Run(Options(faces: true), new FakeEmbedder(), null, faces: new FakeFaceDetector());

        var line = Assert.Single(result.Warning!.Split(Environment.NewLine), l => l.Contains("no preview or poster", StringComparison.Ordinal));
        Assert.StartsWith("1 photos and videos have no preview or poster in the work folder, so they have no embedding.", line, StringComparison.Ordinal);
        Assert.DoesNotContain("faces", line, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unchanged_file_gets_its_embedding_and_faces_in_one_rewrite_of_its_manifest()
    {
        _drive.Write("2019/a.jpg", TestPhotos.Porto());
        Run(new FakeFaceDetector(), faces: false);

        var result = ProcessCommand.Run(Options(faces: true), new FakeEmbedder(), null, faces: new FakeFaceDetector());

        Assert.Equal((1, 1, 1), (result.Skipped, result.Embedded, result.FacesChecked));
        var manifest = Assert.Single(ReadManifests());
        Assert.True(manifest.Embeddings.ContainsKey(ClipImageEmbedder.ModelKey));
        Assert.True(manifest.FacesProcessed);
        Assert.Empty(Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.tmp"));
    }

    [Fact]
    public void A_corrupt_manifest_of_an_unchanged_file_is_one_face_warning_without_an_embedder()
    {
        _drive.Write("2019/a.jpg", TestPhotos.Porto());
        Run(new FakeFaceDetector(), faces: false);
        File.WriteAllText(Assert.Single(Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json")), "{ not json");

        var result = Run(new FakeFaceDetector());

        Assert.Empty(result.EmbeddingProblems);
        Assert.Contains("Could not read its manifest", Assert.Single(result.FaceProblems).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Face_models_are_not_opened_without_faces()
    {
        // A stale marker would be replaced if process verified the model, which it must not do without --faces.
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());
        using var models = new TempFolder();
        var model = ModelCatalog.Get(ModelCatalog.YuNet);
        models.Write(model.File, [1, 2, 3]);
        var marker = models.Write(model.File + ".verified", "stale"u8.ToArray());

        ProcessCommand.Run(Options(faces: false) with { ModelFolder = models.Path });

        Assert.Equal("stale", File.ReadAllText(marker));
    }

    private ProcessOptions Options(bool faces) =>
        new(_drive.Path, _work.Path, "HD da Ana", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None, Faces: faces);

    private ProcessResult Run(FakeFaceDetector detector, bool faces = true) =>
        ProcessCommand.Run(Options(faces), embedder: null, "no model in this test", faces: detector);

    private List<Manifest> ReadManifests() =>
        [.. Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json").Select(file => ManifestJson.Deserialize(File.ReadAllText(file)))];
}
