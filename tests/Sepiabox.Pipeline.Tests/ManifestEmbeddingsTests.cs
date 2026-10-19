using System.Text.Json.Nodes;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;

namespace Sepiabox.Pipeline.Tests;

public sealed class ManifestEmbeddingsTests : IDisposable
{
    private readonly TempFolder _work = new();

    public void Dispose() => _work.Dispose();

    [Fact]
    public void Adds_the_embedding_of_a_photo_from_its_1280_preview()
    {
        var manifest = WithDerivativeFiles(TestManifests.Photo(Source()));
        var embedder = new FakeEmbedder();

        var outcome = ManifestEmbeddings.TryAdd(manifest, _work.Path, embedder);

        Assert.Equal(EmbeddingStatus.Added, outcome.Status);
        Assert.EndsWith("preview-1280.webp", Assert.Single(embedder.Embedded), StringComparison.Ordinal);
        var stored = EmbeddingCodec.Decode(outcome.Manifest!.Embeddings[ClipImageEmbedder.ModelKey].GetValue<string>());
        Assert.Equal(ClipImageEmbedder.Dimensions, stored.Length);
    }

    [Fact]
    public void Adds_the_embedding_of_a_video_from_its_poster()
    {
        var video = TestManifests.Photo(Source()) with
        {
            Kind = MediaKind.Video,
            Derivatives = [TestManifests.Derivative("poster", 1280, 720), TestManifests.Derivative("video-720", 1280, 720)],
        };
        var embedder = new FakeEmbedder();

        var outcome = ManifestEmbeddings.TryAdd(WithDerivativeFiles(video), _work.Path, embedder);

        Assert.Equal(EmbeddingStatus.Added, outcome.Status);
        Assert.EndsWith("poster.webp", Assert.Single(embedder.Embedded), StringComparison.Ordinal);
    }

    [Fact]
    public void Leaves_a_manifest_that_already_has_the_embedding_alone()
    {
        var manifest = WithDerivativeFiles(TestManifests.Photo(Source())) with
        {
            Embeddings = new Dictionary<string, JsonNode> { [ClipImageEmbedder.ModelKey] = Encoded(2f) },
        };
        var embedder = new FakeEmbedder();

        var outcome = ManifestEmbeddings.TryAdd(manifest, _work.Path, embedder);

        Assert.Equal(EmbeddingStatus.Present, outcome.Status);
        Assert.Same(manifest, outcome.Manifest);
        Assert.Empty(embedder.Embedded);
    }

    [Fact]
    public void Reports_no_derivative_when_the_preview_is_not_in_the_work_folder()
    {
        var manifest = TestManifests.Photo(Source());

        var outcome = ManifestEmbeddings.TryAdd(manifest, _work.Path, new FakeEmbedder());

        Assert.Equal(EmbeddingStatus.NoDerivative, outcome.Status);
        Assert.Same(manifest, outcome.Manifest);
    }

    [Fact]
    public void Reports_no_derivative_when_the_manifest_lists_no_preview()
    {
        var manifest = TestManifests.Photo(Source()) with { Derivatives = [TestManifests.Derivative("thumb-256", 256, 192)] };

        var outcome = ManifestEmbeddings.TryAdd(WithDerivativeFiles(manifest), _work.Path, new FakeEmbedder());

        Assert.Equal(EmbeddingStatus.NoDerivative, outcome.Status);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(0f)]
    public void Reports_a_vector_that_cannot_be_stored_without_changing_the_manifest(float value)
    {
        var manifest = WithDerivativeFiles(TestManifests.Photo(Source()));
        var embedder = new FakeEmbedder { Output = Enumerable.Repeat(value, ClipImageEmbedder.Dimensions).ToArray() };

        var outcome = ManifestEmbeddings.TryAdd(manifest, _work.Path, embedder);

        Assert.Equal(EmbeddingStatus.Failed, outcome.Status);
        Assert.Same(manifest, outcome.Manifest);
        Assert.Contains("clip-vit-b32", outcome.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_a_vector_of_the_wrong_size()
    {
        var embedder = new FakeEmbedder { Output = [1f, 2f, 3f] };

        var outcome = ManifestEmbeddings.TryAdd(WithDerivativeFiles(TestManifests.Photo(Source())), _work.Path, embedder);

        Assert.Equal(EmbeddingStatus.Failed, outcome.Status);
        Assert.Contains("3 values instead of 512", outcome.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_an_unreadable_preview()
    {
        var embedder = new FakeEmbedder { Failure = path => new UnauthorizedAccessException($"Access to {path} is denied.") };

        var outcome = ManifestEmbeddings.TryAdd(WithDerivativeFiles(TestManifests.Photo(Source())), _work.Path, embedder);

        Assert.Equal(EmbeddingStatus.Failed, outcome.Status);
        Assert.Contains("is denied", outcome.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_the_embeddings_of_other_models_in_key_order()
    {
        var manifest = WithDerivativeFiles(TestManifests.Photo(Source())) with
        {
            Embeddings = new Dictionary<string, JsonNode> { ["zz-model"] = Encoded(1f), ["aa-model"] = Encoded(3f) },
        };

        var outcome = ManifestEmbeddings.TryAdd(manifest, _work.Path, new FakeEmbedder());

        Assert.Equal(["aa-model", "clip-vit-b32", "zz-model"], outcome.Manifest!.Embeddings.Keys);
    }

    [Fact]
    public void A_manifest_file_is_rewritten_with_the_embedding()
    {
        var path = WriteManifest(WithDerivativeFiles(TestManifests.Photo(Source())));

        var outcome = ManifestEmbeddings.TryAddToFile(path, _work.Path, new FakeEmbedder());

        Assert.Equal(EmbeddingStatus.Added, outcome.Status);
        Assert.True(ManifestJson.Deserialize(File.ReadAllText(path)).Embeddings.ContainsKey(ClipImageEmbedder.ModelKey));
    }

    [Fact]
    public void A_manifest_file_that_needs_nothing_is_not_rewritten()
    {
        var path = WriteManifest(TestManifests.Photo(Source()));
        var before = File.ReadAllText(path);
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var outcome = ManifestEmbeddings.TryAddToFile(path, _work.Path, new FakeEmbedder());

        Assert.Equal(EmbeddingStatus.NoDerivative, outcome.Status);
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void A_corrupt_manifest_file_is_reported_and_left_as_it_is()
    {
        var path = _work.Write("manifests/corrupt.json", "{ not json"u8.ToArray());

        var outcome = ManifestEmbeddings.TryAddToFile(path, _work.Path, new FakeEmbedder());

        Assert.Equal(EmbeddingStatus.Failed, outcome.Status);
        Assert.Null(outcome.Manifest);
        Assert.Contains("Could not read the manifest", outcome.Problem, StringComparison.Ordinal);
        Assert.Equal("{ not json", File.ReadAllText(path));
    }

    [Fact]
    public void A_missing_manifest_file_is_reported()
    {
        var outcome = ManifestEmbeddings.TryAddToFile(Path.Combine(_work.Path, "manifests", "gone.json"), _work.Path, new FakeEmbedder());

        Assert.Equal(EmbeddingStatus.Failed, outcome.Status);
    }

    private static ManifestSource Source() => TestManifests.Source("HD da Ana", "2019/praia.jpg");

    private static JsonValue Encoded(float value) => JsonValue.Create(EmbeddingCodec.Encode([value]));

    private Manifest WithDerivativeFiles(Manifest manifest)
    {
        foreach (var derivative in manifest.Derivatives)
        {
            _work.Write(derivative.Path, [1, 2, 3]);
        }

        return manifest;
    }

    private string WriteManifest(Manifest manifest)
    {
        var path = Path.Combine(_work.Path, "manifests", manifest.Id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ManifestJson.WriteFile(path, manifest);
        return path;
    }
}
