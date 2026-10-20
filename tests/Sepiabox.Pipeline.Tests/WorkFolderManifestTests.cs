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

    [Fact]
    public void Gives_a_category_without_the_file_details_when_a_manifest_is_corrupt()
    {
        var path = _work.Write("manifests/corrupt.json", "{ not json"u8.ToArray());

        var outcome = WorkFolderManifest.TryAdd(path, _work.Path, new FakeEmbedder(), faces: null).Embedding!;

        Assert.Equal("Could not read its manifest (JsonException)", outcome.Category);
    }

    [Fact]
    public void A_manifest_file_is_rewritten_with_the_embedding()
    {
        var path = WriteManifest(Photo());

        var outcome = WorkFolderManifest.TryAdd(path, _work.Path, new FakeEmbedder(), faces: null).Embedding!;

        Assert.Equal(EmbeddingStatus.Added, outcome.Status);
        Assert.True(ManifestJson.Deserialize(File.ReadAllText(path)).Embeddings.ContainsKey(ClipImageEmbedder.ModelKey));
    }

    [Fact]
    public void A_manifest_file_that_needs_nothing_is_not_rewritten()
    {
        var path = WriteManifest(Photo(), withDerivatives: false);
        var before = File.ReadAllText(path);
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var outcome = WorkFolderManifest.TryAdd(path, _work.Path, new FakeEmbedder(), faces: null).Embedding!;

        Assert.Equal(EmbeddingStatus.NoDerivative, outcome.Status);
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void A_corrupt_manifest_file_is_reported_and_left_as_it_is()
    {
        var path = _work.Write("manifests/corrupt.json", "{ not json"u8.ToArray());

        var outcome = WorkFolderManifest.TryAdd(path, _work.Path, new FakeEmbedder(), faces: null).Embedding!;

        Assert.Equal(EmbeddingStatus.Failed, outcome.Status);
        Assert.Null(outcome.Manifest);
        Assert.Contains("Could not read its manifest", outcome.Problem, StringComparison.Ordinal);
        Assert.Equal("{ not json", File.ReadAllText(path));
    }

    [Fact]
    public void Reports_a_missing_manifest_when_the_file_is_gone()
    {
        var outcome = WorkFolderManifest.TryAdd(Path.Combine(_work.Path, "manifests", "gone.json"), _work.Path, new FakeEmbedder(), faces: null).Embedding!;

        Assert.Equal(EmbeddingStatus.ManifestMissing, outcome.Status);
    }

    [Fact]
    public void Reports_a_missing_manifest_when_its_folder_is_gone()
    {
        var outcome = WorkFolderManifest.TryAdd(Path.Combine(_work.Path, "nowhere", "gone.json"), _work.Path, new FakeEmbedder(), faces: null).Embedding!;

        Assert.Equal(EmbeddingStatus.ManifestMissing, outcome.Status);
    }

    private static Manifest Photo() => TestManifests.Photo(TestManifests.Source("HD da Ana", "2019/praia.jpg"));

    private string WriteManifest(Manifest manifest, bool withDerivatives = true)
    {
        foreach (var derivative in withDerivatives ? manifest.Derivatives : [])
        {
            _work.Write(derivative.Path, [1, 2, 3]);
        }

        var path = Path.Combine(_work.Path, "manifests", manifest.Id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ManifestJson.WriteFile(path, manifest);
        return path;
    }
}
