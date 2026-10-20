using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Processing;

namespace Sepiabox.Pipeline.Tests;

public sealed class WorkFolderManifestTests : IDisposable
{
    private readonly TempFolder _work = new();

    public void Dispose() => _work.Dispose();

    [Fact]
    public void Adds_the_embedding_and_the_faces_in_one_write()
    {
        var path = WriteManifest(Photo());

        var outcome = WorkFolderManifest.TryAdd(path, _work.Path, new FakeEmbedder(), new FakeFaceDetector());

        Assert.Equal((EmbeddingStatus.Added, EmbeddingStatus.Added), (outcome.Embedding!.Status, outcome.Faces!.Status));
        var written = ManifestJson.Deserialize(File.ReadAllText(path));
        Assert.True(written.Embeddings.ContainsKey(ClipImageEmbedder.ModelKey));
        Assert.True(written.FacesProcessed);
        Assert.Single(written.Faces);
    }

    [Fact]
    public void Does_not_write_when_nothing_was_added()
    {
        var path = WriteManifest(Photo() with { FacesProcessed = true });
        var old = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(path, old);

        var outcome = WorkFolderManifest.TryAdd(path, _work.Path, embedder: null, new FakeFaceDetector());

        Assert.Null(outcome.Embedding);
        Assert.Equal(EmbeddingStatus.Present, outcome.Faces!.Status);
        Assert.Equal(old, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Reports_a_manifest_that_cannot_be_read_once_on_the_embedding()
    {
        var path = _work.Write("manifests/broken.json", "{ not json"u8.ToArray());

        var outcome = WorkFolderManifest.TryAdd(path, _work.Path, new FakeEmbedder(), new FakeFaceDetector());

        Assert.Equal(EmbeddingStatus.Failed, outcome.Embedding!.Status);
        Assert.Contains("Could not read its manifest", outcome.Embedding.Problem, StringComparison.Ordinal);
        Assert.Null(outcome.Faces);
    }

    [Fact]
    public void Reports_a_manifest_that_cannot_be_read_on_the_faces_without_an_embedder()
    {
        var path = _work.Write("manifests/broken.json", "{ not json"u8.ToArray());

        var outcome = WorkFolderManifest.TryAdd(path, _work.Path, embedder: null, new FakeFaceDetector());

        Assert.Null(outcome.Embedding);
        Assert.Equal(EmbeddingStatus.Failed, outcome.Faces!.Status);
    }

    [Fact]
    public void Reports_a_missing_manifest_for_both()
    {
        var outcome = WorkFolderManifest.TryAdd(Path.Combine(_work.Path, "gone.json"), _work.Path, new FakeEmbedder(), new FakeFaceDetector());

        Assert.Equal((EmbeddingStatus.ManifestMissing, EmbeddingStatus.ManifestMissing), (outcome.Embedding!.Status, outcome.Faces!.Status));
    }

    private static Manifest Photo() => TestManifests.Photo(TestManifests.Source("HD da Ana", "2019/praia.jpg"));

    private string WriteManifest(Manifest manifest)
    {
        foreach (var derivative in manifest.Derivatives)
        {
            _work.Write(derivative.Path, [1, 2, 3]);
        }

        var path = Path.Combine(_work.Path, "manifests", manifest.Id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ManifestJson.WriteFile(path, manifest);
        return path;
    }
}
