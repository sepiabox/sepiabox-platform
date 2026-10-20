using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog;
using Sepiabox.Search.Clip;
using Sepiabox.Search.Models;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The web in Production with and without the CLIP text model (S-056, D-071), on a real catalog: the model is loaded
/// before requests, a missing or corrupted one is one error, a Degraded health check and a 503, and nothing else breaks.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class ProductionTextModelTests(CatalogFixture catalog) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "sepiabox-web-models-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Stays_ready_with_one_error_and_answers_503_when_the_model_is_missing()
    {
        await AssertDegradedAsync(_folder, "is not in");
    }

    [Fact]
    public async Task Stays_ready_with_one_error_and_answers_503_when_the_model_is_corrupted()
    {
        Directory.CreateDirectory(_folder);
        // Files with the pinned names but not the pinned content, as a broken image would carry.
        await File.WriteAllTextAsync(Path.Combine(_folder, "clip-vit-b32-text.onnx"), "not a model");
        await File.WriteAllTextAsync(Path.Combine(_folder, "clip-bpe-simple-vocab-16e6.txt.gz"), "not merges");

        await AssertDegradedAsync(_folder, "SHA-256");
    }

    [ClipTextModelFact]
    public async Task Loads_the_model_before_requests_and_answers_a_description_search_when_it_is_there()
    {
        var logs = new CapturedLogs();
        await using var web = await WebAsync(ModelFolder.Default(), logs, withEmbeddings: true);
        using var http = web.CreateSignedInClient();

        using var health = await http.GetAsync(new Uri("/health", UriKind.Relative));
        using var search = await http.PostAsync(new Uri("/api/search", UriKind.Relative), Json("""{"filters": {"visual": {"text": "a photo", "minScore": 0.9}}}"""));

        Assert.Equal("Healthy", await health.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        Assert.Contains(Ids.Of(1), await search.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(logs.Entries, e => e.Message.StartsWith("CLIP text model ready", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Error);
    }

    private async Task AssertDegradedAsync(string folder, string reason)
    {
        var logs = new CapturedLogs();
        await using var web = await WebAsync(folder, logs, withEmbeddings: false);
        using var http = web.CreateSignedInClient();

        using var health = await http.GetAsync(new Uri("/health", UriKind.Relative));
        using var alive = await http.GetAsync(new Uri("/alive", UriKind.Relative));
        using var page = await http.GetAsync(new Uri("/", UriKind.Relative));
        using var search = await http.PostAsync(new Uri("/api/search", UriKind.Relative), Json("""{"filters": {"visual": {"text": "snow"}}}"""));
        using var filters = await http.PostAsync(new Uri("/api/search", UriKind.Relative), Json("""{"filters": {"years": [2019]}}"""));

        // Degraded answers 200: the probes keep the web ready.
        Assert.Equal((HttpStatusCode.OK, "Degraded"), (health.StatusCode, await health.Content.ReadAsStringAsync()));
        Assert.Equal((HttpStatusCode.OK, "Healthy"), (alive.StatusCode, await alive.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.DoesNotContain("name=\"text\"", await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, search.StatusCode);
        Assert.Equal(HttpStatusCode.OK, filters.StatusCode);
        var error = Assert.Single(logs.Entries, e => e.Level >= LogLevel.Error);
        Assert.StartsWith("Search by description is off", error.Message, StringComparison.Ordinal);
        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    private async Task<WebFactory> WebAsync(string folder, CapturedLogs logs, bool withEmbeddings)
    {
        string connection;
        await using (var db = await catalog.CreateMigratedDatabaseAsync())
        {
            db.Assets.Add(CatalogData.Photo(Ids.Of(1), new DateTime(2019, 7, 14)));
            if (withEmbeddings)
            {
                // The text's own embedding, so the photo is certainly above any threshold.
                using var encoder = ClipTextEncoder.TryCreate(folder, out var warning) ?? throw new InvalidOperationException(warning);
                var vector = encoder.Encode("a photo");
                db.AssetEmbeddings.Add(new AssetEmbedding { AssetId = Ids.Of(1), Model = "clip-vit-b32", Embedding = new Pgvector.Vector(vector) });
            }

            await db.SaveChangesAsync();
            connection = catalog.ConnectionStringFor(db);
        }

        var web = new WebFactory(connection)
        {
            Environment = "Production",
            Logs = logs,
            Settings = new()
            {
                ["Search:ModelFolder"] = folder,
                // The fixture's Azurite, so /health's storage check passes and only the model's can degrade it.
                ["ConnectionStrings:blobs"] = catalog.BlobConnectionString,
            },
        };
        // Starting the host starts the warm-up, as the container does; the tests wait for it to finish.
        var warmUp = web.Services.GetServices<IHostedService>().OfType<Sepiabox.Web.Services.TextModelWarmUp>().Single();
        await warmUp.ExecuteTask!;
        return web;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Category, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<(LogLevel, string, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (category.StartsWith("Sepiabox", StringComparison.Ordinal))
                {
                    entries.Enqueue((logLevel, category, formatter(state, exception)));
                }
            }
        }
    }
}

/// <summary>Needs the real CLIP text model: skipped when it is not downloaded, unless CI requires the models.</summary>
internal sealed class ClipTextModelFactAttribute : FactAttribute
{
    public ClipTextModelFactAttribute()
    {
        if (!ClipTextEncoder.FilesPresent(ModelFolder.Default()) && Environment.GetEnvironmentVariable("SEPIABOX_REQUIRE_MODELS") != "1")
        {
            Skip = $"The CLIP text model is not in {ModelFolder.Default()}; run `sepiabox-ingest models download`.";
        }
    }
}
