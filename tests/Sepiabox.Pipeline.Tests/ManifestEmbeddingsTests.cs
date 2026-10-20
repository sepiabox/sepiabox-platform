using System.Text.Json.Nodes;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Processing;

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
    public void Reports_not_listed_when_the_manifest_lists_no_preview()
    {
        var manifest = TestManifests.Photo(Source()) with { Derivatives = [TestManifests.Derivative("thumb-256", 256, 192)] };

        var outcome = ManifestEmbeddings.TryAdd(WithDerivativeFiles(manifest), _work.Path, new FakeEmbedder());

        Assert.Equal(EmbeddingStatus.NotListed, outcome.Status);
    }

    [Fact]
    public void Reports_a_failure_when_the_embedder_throws_something_unexpected()
    {
        var embedder = new FakeEmbedder { Failure = _ => new InvalidOperationException("The session was disposed.") };

        var outcome = ManifestEmbeddings.TryAdd(WithDerivativeFiles(TestManifests.Photo(Source())), _work.Path, embedder);

        Assert.Equal(EmbeddingStatus.Failed, outcome.Status);
        Assert.Contains("The session was disposed.", outcome.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Gives_a_category_without_the_file_details_when_embedding_fails()
    {
        var embedder = new FakeEmbedder { Failure = path => new UnauthorizedAccessException($"Access to {path} is denied.") };

        var outcome = ManifestEmbeddings.TryAdd(WithDerivativeFiles(TestManifests.Photo(Source())), _work.Path, embedder);

        Assert.Equal("Could not compute the clip-vit-b32 embedding (UnauthorizedAccessException)", outcome.Category);
        Assert.Contains("is denied", outcome.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Gives_a_category_without_the_file_details_when_a_manifest_is_corrupt()
    {
        var path = _work.Write("manifests/corrupt.json", "{ not json"u8.ToArray());

        var outcome = WorkFolderManifest.TryAdd(path, _work.Path, new FakeEmbedder(), faces: null).Embedding!;

        Assert.Equal("Could not read its manifest (JsonException)", outcome.Category);
    }

    [Fact]
    public void Lets_out_of_memory_through_when_the_embedder_runs_out()
    {
        // InsufficientMemoryException is an OutOfMemoryException that code may throw itself.
        var embedder = new FakeEmbedder { Failure = _ => new InsufficientMemoryException() };

        Assert.Throws<InsufficientMemoryException>(
            () => ManifestEmbeddings.TryAdd(WithDerivativeFiles(TestManifests.Photo(Source())), _work.Path, embedder));
    }

    [Fact]
    public void Lets_cancellation_through_when_the_embedder_is_cancelled()
    {
        var embedder = new FakeEmbedder { Failure = _ => new OperationCanceledException() };

        Assert.Throws<OperationCanceledException>(
            () => ManifestEmbeddings.TryAdd(WithDerivativeFiles(TestManifests.Photo(Source())), _work.Path, embedder));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(0f)]
    public void Reports_a_failure_when_the_embedder_returns_a_vector_that_cannot_be_stored(float value)
    {
        var embedder = new FakeEmbedder { RawOutput = Enumerable.Repeat(value, ClipImageEmbedder.Dimensions).ToArray() };

        var outcome = ManifestEmbeddings.TryAdd(WithDerivativeFiles(TestManifests.Photo(Source())), _work.Path, embedder);

        Assert.Equal(EmbeddingStatus.Failed, outcome.Status);
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

        var outcome = WorkFolderManifest.TryAdd(path, _work.Path, new FakeEmbedder(), faces: null).Embedding!;

        Assert.Equal(EmbeddingStatus.Added, outcome.Status);
        Assert.True(ManifestJson.Deserialize(File.ReadAllText(path)).Embeddings.ContainsKey(ClipImageEmbedder.ModelKey));
    }

    [Fact]
    public void A_manifest_file_that_needs_nothing_is_not_rewritten()
    {
        var path = WriteManifest(TestManifests.Photo(Source()));
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

    [Fact]
    public void Leaves_only_the_manifest_when_writing_succeeds()
    {
        var path = WriteManifest(TestManifests.Photo(Source()));

        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void Leaves_no_temporary_file_when_writing_fails()
    {
        var folder = Path.Combine(_work.Path, "manifests");
        var path = Path.Combine(folder, "taken.json");
        Directory.CreateDirectory(path);

        // IOException on Linux, UnauthorizedAccessException on Windows: either way the write fails.
        Assert.NotNull(Record.Exception(() => ManifestJson.WriteFile(path, TestManifests.Photo(Source()))));

        Assert.Empty(Directory.GetFiles(folder));
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
