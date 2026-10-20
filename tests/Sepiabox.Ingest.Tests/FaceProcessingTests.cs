using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Faces;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Ingest.Tests;

/// <summary><c>process</c> with the real YuNet and SFace models on a public-domain portrait (S-018).</summary>
[Trait("Category", "Integration")]
public sealed class FaceProcessingTests : IDisposable
{
    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [FacesFact]
    public void Process_with_faces_stores_the_face_of_the_portrait()
    {
        _drive.Write("2019/retrato.jpg", File.ReadAllBytes(Path.Combine("TestData", "face-portrait.jpg")));

        var result = ProcessCommand.Run(Options(faces: true));

        Assert.Equal((1, 1, 1), (result.Processed, result.FacesChecked, result.FacesFound));
        Assert.Empty(result.FaceProblems);
        var manifest = Assert.Single(ReadManifests());
        Assert.True(manifest.FacesProcessed);
        var face = Assert.Single(manifest.Faces);
        Assert.InRange(face.Score, FaceDetector.MinimumScore, 1);
        Assert.True(face.Box[0] + face.Box[2] <= 1 && face.Box[1] + face.Box[3] <= 1, $"box {string.Join(", ", face.Box)}");
        Assert.Equal(FaceDetector.Dimensions, EmbeddingCodec.Decode(face.Embedding).Length);
        Assert.True(new ManifestValidator().TryValidate(File.ReadAllText(ManifestFile()), out _, out var problem), problem);
    }

    [FacesFact]
    public void Process_without_faces_leaves_faces_unprocessed_even_with_the_models_downloaded()
    {
        _drive.Write("2019/retrato.jpg", File.ReadAllBytes(Path.Combine("TestData", "face-portrait.jpg")));

        var result = ProcessCommand.Run(Options(faces: false));

        Assert.Equal((1, 0), (result.Processed, result.FacesChecked));
        var manifest = Assert.Single(ReadManifests());
        Assert.False(manifest.FacesProcessed);
        Assert.Empty(manifest.Faces);
    }

    private ProcessOptions Options(bool faces) =>
        new(_drive.Path, _work.Path, "HD da Ana", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.Folder, Faces: faces);

    private string ManifestFile() => Assert.Single(Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json"));

    private List<Manifest> ReadManifests() => [ManifestJson.Deserialize(File.ReadAllText(ManifestFile()))];
}
