using Sepiabox.Ingest.Processing;
using Sepiabox.Pipeline.Embeddings;
using Sepiabox.Pipeline.Manifests;

namespace Sepiabox.Ingest.Tests;

/// <summary>How process reacts to embeddings that work or fail, with a fake embedder instead of the CLIP model.</summary>
public sealed class ProcessEmbeddingTests : IDisposable
{
    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();

    public void Dispose()
    {
        _drive.Dispose();
        _work.Dispose();
    }

    [Fact]
    public void A_failed_embedding_leaves_a_new_file_done_and_is_a_warning_not_a_failure()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());
        var failing = new FakeEmbedder { Failure = _ => new EmbeddingException("The embedding is a zero vector, which has no direction.") };

        var result = Run(failing);

        Assert.Equal((1, 0), (result.Processed, result.Embedded));
        Assert.Empty(result.Problems);
        Assert.Contains("zero vector", Assert.Single(result.EmbeddingProblems).Reason, StringComparison.Ordinal);

        var retry = Run(new FakeEmbedder());

        Assert.Equal((0, 1, 1), (retry.Processed, retry.Skipped, retry.Embedded));
        Assert.Empty(retry.EmbeddingProblems);
    }

    [Fact]
    public void An_unexpected_error_from_the_embedder_on_a_new_file_does_not_mark_it_failed()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());

        var result = Run(new FakeEmbedder { Failure = _ => new InvalidOperationException("Something inside the model broke.") });

        Assert.Equal(1, result.Processed);
        Assert.Empty(result.Problems);
        Assert.Single(result.EmbeddingProblems);
        Assert.Equal(1, Run(new FakeEmbedder()).Skipped);
    }

    [Fact]
    public void A_corrupt_manifest_of_an_unchanged_file_is_reported_and_the_run_goes_on()
    {
        _drive.Write("2019/a.jpg", TestPhotos.Porto());
        _drive.Write("2019/b.jpg", TestPhotos.Porto("tomato"));
        Run(embedder: null);
        var manifests = Directory.GetFiles(Path.Combine(_work.Path, "manifests"), "*.json").Order(StringComparer.Ordinal).ToList();
        File.WriteAllText(manifests[0], "{ not json");

        var result = Run(new FakeEmbedder());

        Assert.Equal((2, 1), (result.Skipped, result.Embedded));
        Assert.Empty(result.Problems);
        Assert.Contains("Could not read the manifest", Assert.Single(result.EmbeddingProblems).Reason, StringComparison.Ordinal);
        Assert.True(ManifestJson.Deserialize(File.ReadAllText(manifests[1])).Embeddings.ContainsKey(ClipImageEmbedder.ModelKey));
    }

    [Fact]
    public void An_unexpected_error_from_the_embedder_on_an_unchanged_file_does_not_stop_the_run()
    {
        _drive.Write("2019/a.jpg", TestPhotos.Porto());
        _drive.Write("2019/b.jpg", TestPhotos.Porto("tomato"));
        Run(embedder: null);
        var calls = 0;

        var result = Run(new FakeEmbedder { Failure = _ => calls++ == 0 ? new InvalidOperationException("The model returned nothing.") : null });

        Assert.Equal((2, 1), (result.Skipped, result.Embedded));
        Assert.Empty(result.Problems);
        Assert.Single(result.EmbeddingProblems);
    }

    [Fact]
    public void Embeddings_added_counts_only_embeddings_that_were_added()
    {
        _drive.Write("2019/praia.jpg", TestPhotos.Porto());

        Assert.Equal(1, Run(new FakeEmbedder()).Embedded);
        Assert.Equal(0, Run(new FakeEmbedder()).Embedded);
    }

    [Fact]
    public void Previews_missing_from_the_work_folder_give_one_warning_for_all_of_them()
    {
        _drive.Write("2019/a.jpg", TestPhotos.Porto());
        _drive.Write("2019/b.jpg", TestPhotos.Porto("tomato"));
        Run(embedder: null);
        Directory.Delete(Path.Combine(_work.Path, "derivatives"), recursive: true);

        var result = Run(new FakeEmbedder());

        Assert.Equal(0, result.Embedded);
        Assert.Empty(result.EmbeddingProblems);
        Assert.Contains("2 photos and videos have no preview or poster", result.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Progress_counts_unchanged_files_too()
    {
        _drive.Write("2019/a.jpg", TestPhotos.Porto());
        _drive.Write("2019/b.jpg", TestPhotos.Porto("tomato"));
        Run(embedder: null);
        var reported = new List<int>();

        Run(new FakeEmbedder(), new Recorder(reported));

        Assert.Equal([1, 2], reported);
    }

    private ProcessResult Run(FakeEmbedder? embedder, IProgress<int>? progress = null) =>
        ProcessCommand.Run(
            new ProcessOptions(_drive.Path, _work.Path, "HD da Ana", FfmpegFolder: TestFfmpeg.None, ModelFolder: TestModels.None),
            embedder,
            embedder is null ? "no model in this test" : null,
            progress);

    private sealed class Recorder(List<int> reported) : IProgress<int>
    {
        public void Report(int value) => reported.Add(value);
    }
}
