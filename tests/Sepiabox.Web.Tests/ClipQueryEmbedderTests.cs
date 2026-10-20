using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog.Search;
using Sepiabox.Search.Clip;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The web's CLIP text encoder without the model (S-016): unavailable, never an error page.</summary>
public sealed class ClipQueryEmbedderTests
{
    [Fact]
    public void Is_unavailable_without_the_model_files_and_never_an_error()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sepiabox-tests-no-models-" + Guid.NewGuid().ToString("N"));
        using var embedder = new ClipQueryEmbedder(folder, new ListLogger<ClipQueryEmbedder>());

        Assert.False(embedder.IsAvailable);
        Assert.Throws<VisualSearchUnavailableException>(() => embedder.Embed("snow"));
        Assert.False(embedder.IsAvailable);
    }

    [Fact]
    public void Shows_the_box_once_missing_files_arrive_without_remembering_their_absence()
    {
        var present = false;
        var log = new ListLogger<ClipQueryEmbedder>();
        using var embedder = Embedder(log, () => present ? Loaded() : Failed(ClipLoadFailure.Missing), () => present);

        Assert.Throws<VisualSearchUnavailableException>(() => embedder.Embed("snow"));
        present = true;

        Assert.True(embedder.IsAvailable);
        Assert.Equal(Vector, embedder.Embed("snow"));
        Assert.DoesNotContain(log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Stays_off_until_a_restart_after_a_file_that_is_not_the_pinned_one()
    {
        var clock = new MovableTime(DateTimeOffset.UnixEpoch);
        var loads = 0;
        var log = new ListLogger<ClipQueryEmbedder>();
        using var embedder = Embedder(log, () => { loads++; return Failed(ClipLoadFailure.Invalid); }, () => true, clock);

        Assert.Throws<VisualSearchUnavailableException>(() => embedder.Embed("snow"));
        clock.Now += TimeSpan.FromHours(1);

        Assert.False(embedder.IsAvailable);
        Assert.Throws<VisualSearchUnavailableException>(() => embedder.Embed("snow"));
        Assert.Equal(1, loads);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Tries_again_after_five_minutes_when_a_file_could_not_be_opened()
    {
        var clock = new MovableTime(DateTimeOffset.UnixEpoch);
        var readable = false;
        using var embedder = Embedder(
            new ListLogger<ClipQueryEmbedder>(), () => readable ? Loaded() : Failed(ClipLoadFailure.Unreadable), () => true, clock);

        Assert.Throws<VisualSearchUnavailableException>(() => embedder.Embed("snow"));
        readable = true;
        clock.Now += ClipQueryEmbedder.RetryAfter - TimeSpan.FromSeconds(1);
        Assert.False(embedder.IsAvailable);
        Assert.Throws<VisualSearchUnavailableException>(() => embedder.Embed("snow"));

        clock.Now += TimeSpan.FromSeconds(1);
        Assert.True(embedder.IsAvailable);
        Assert.Equal(Vector, embedder.Embed("snow"));
    }

    [Fact]
    public async Task Answers_is_available_at_once_while_the_model_is_loading()
    {
        using var loading = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var embedder = Embedder(new ListLogger<ClipQueryEmbedder>(), () =>
        {
            loading.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            return Loaded();
        }, () => true);

        var search = Task.Run(() => embedder.Embed("snow"));
        Assert.True(loading.Wait(TimeSpan.FromSeconds(10)));

        // A timeline render while ONNX Runtime opens the model: no waiting on the load.
        var render = Task.Run(() => embedder.IsAvailable);
        var answered = await Task.WhenAny(render, Task.Delay(TimeSpan.FromSeconds(5))) == render;
        release.Set();

        Assert.True(answered, "IsAvailable waited for the model to load.");
        Assert.True(await render);
        Assert.Equal(Vector, await search);
    }

    private static readonly float[] Vector = [1, 0];

    private static ClipQueryEmbedder Embedder(
        ListLogger<ClipQueryEmbedder> log, Func<(ITextEncoder?, string?, ClipLoadFailure)> load, Func<bool> present, TimeProvider? clock = null) =>
        new("unused", (out string? warning, out ClipLoadFailure failure) =>
        {
            var (encoder, message, kind) = load();
            (warning, failure) = (message, kind);
            return encoder;
        }, present, log, clock);

    private static (ITextEncoder?, string?, ClipLoadFailure) Loaded() => (new FakeTextEncoder(), null, ClipLoadFailure.None);

    private static (ITextEncoder?, string?, ClipLoadFailure) Failed(ClipLoadFailure failure) => (null, $"Failed: {failure}.", failure);

    private sealed class FakeTextEncoder : ITextEncoder
    {
        public float[] Encode(string text) => Vector;

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Reads_the_models_from_search_model_folder_when_it_is_set()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Search:ModelFolder"] = "/srv/models" })
            .Build();

        Assert.Equal("/srv/models", SearchSetup.ModelFolder(configuration));
        Assert.Equal(
            Sepiabox.Search.Models.ModelFolder.Default(),
            SearchSetup.ModelFolder(new ConfigurationBuilder().Build()));
    }
}
