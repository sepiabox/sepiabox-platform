using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The web's CLIP text encoder without the model (S-016): unavailable, never an error page.</summary>
public sealed class ClipQueryEmbedderTests
{
    [Fact]
    public void Is_unavailable_and_refuses_to_embed_once_with_a_warning_when_the_model_is_missing()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sepiabox-tests-no-models-" + Guid.NewGuid().ToString("N"));
        var log = new ListLogger<ClipQueryEmbedder>();
        using var embedder = new ClipQueryEmbedder(folder, log);

        Assert.False(embedder.IsAvailable);
        Assert.Throws<VisualSearchUnavailableException>(() => embedder.Embed("snow"));
        Assert.Throws<VisualSearchUnavailableException>(() => embedder.Embed("snow"));

        var warning = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("Search by description is off", warning.Message, StringComparison.Ordinal);
        Assert.False(embedder.IsAvailable);
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
