using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Faces;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Ingest.Tests;

/// <summary>With the real models (S-063): the embedding and the faces of an index-only work folder come from its 2048 px preview.</summary>
[Trait("Category", "Integration")]
public sealed class IndexOnlyModelTests : IDisposable
{
    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [FacesFact]
    public void Finds_the_faces_in_the_2048_preview_with_boxes_relative_to_it()
    {
        _drive.Write("2019/retrato.jpg", File.ReadAllBytes(Path.Combine("TestData", "face-portrait.jpg")));

        var result = ProcessCommand.Run(Options(faces: true));

        Assert.Empty(result.Problems);
        Assert.Empty(result.FaceProblems);
        var manifest = Assert.Single(ReadManifests());
        Assert.False(manifest.IsOriginalStored);
        Assert.DoesNotContain(manifest.Derivatives, d => d.Name == "preview-1280");
        var preview = Assert.Single(manifest.Derivatives, d => d.Name == "preview-2048");
        Assert.True(manifest.FacesProcessed);
        Assert.NotEmpty(manifest.Faces);

        using var detector = FaceDetector.TryCreate(TestModels.Folder, out var warning);
        Assert.NotNull(detector);
        Assert.Null(warning);
        var expected = detector.Detect(Path.Combine(_work.Path, preview.Path)).Select(ManifestFaces.ToManifest).ToList();
        Assert.Equal(expected.Select(f => f.Box), manifest.Faces.Select(f => f.Box));
        Assert.All(manifest.Faces, face => Assert.True(face.Box.All(v => v is >= 0 and <= 1)));
    }

    [ClipFact]
    public void Computes_the_embedding_from_the_2048_preview()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Sized(3000, 2000));

        var result = ProcessCommand.Run(Options(faces: false));

        Assert.Equal(1, result.Embedded);
        var manifest = Assert.Single(ReadManifests());
        var preview = Assert.Single(manifest.Derivatives, d => d.Name == "preview-2048");
        using var embedder = ClipImageEmbedder.TryCreate(TestModels.Folder, out var warning);
        Assert.NotNull(embedder);
        Assert.Null(warning);
        var expected = EmbeddingCodec.Normalized(embedder.Embed(Path.Combine(_work.Path, preview.Path)));
        Assert.Equal(EmbeddingCodec.Encode(expected), manifest.Embeddings[ClipImageEmbedder.ModelKey].GetValue<string>());
    }

    private ProcessOptions Options(bool faces) =>
        new(_drive.Path, _work.Path, "HD da Ana", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.Folder, Faces: faces, IndexOnly: true);

    private List<Manifest> ReadManifests() =>
        [.. Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json").Select(file => ManifestJson.Deserialize(File.ReadAllText(file)))];
}
