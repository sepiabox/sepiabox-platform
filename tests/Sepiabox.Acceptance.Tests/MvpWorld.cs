using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;
using Sepiabox.Ingest.Processing;
using Sepiabox.Ingest.Reporting;
using Sepiabox.Ingest.Uploading;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Video;
using Sepiabox.Search.Questions;
using Sepiabox.Storage;
using Sepiabox.Web;
using Sepiabox.Web.Services;
using Sepiabox.Worker.Importing;
using Sepiabox.Worker.Places;

namespace Sepiabox.Acceptance.Tests;

/// <summary>
/// The MVP's whole path once (S-054): the fixture library (S-061) on a "drive", <c>process</c> into a work folder,
/// <c>report --json</c> (S-062), <c>upload</c> to Azurite, the GeoNames sample (S-012), <c>import --once</c> into a test
/// PostgreSQL, and the web built by its own <c>Program.cs</c> on that catalog, with no HTTP and no sign-in. Built once
/// for all the questions; nothing leaves the machine but, in the live run, the questions' text.
/// </summary>
public sealed class MvpWorld : IAsyncLifetime, IDisposable
{
    public const int Seed = 54;
    public const string Person = "acceptance@example.com";

    private readonly CatalogFixture _catalog = new();
    private readonly TempFolder _drive = new();
    private readonly TempFolder _work = new();
    private readonly TempFolder _elsewhere = new();
    private readonly ForwardingTranslator _translator = new();
    private readonly SemaphoreSlim _asking = new(1, 1);
    private WebApplicationFactory<QuestionService>? _web;

    /// <summary>What the generator wrote: the reference for cities, and for the totals with <see cref="Report"/>.</summary>
    internal FixtureExpectations Expected { get; private set; } = null!;

    /// <summary><c>report --json</c> on the work folder: the reference for years, kinds and the undated.</summary>
    public JsonNode Report { get; private set; } = null!;

    public ImportResult Import { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _catalog.InitializeAsync();
        var tools = FfmpegTools.Find(null) ?? throw new InvalidOperationException("The acceptance test needs ffmpeg and ffprobe on the PATH.");
        Expected = FixtureLibrary.Write(_drive.Path, Seed, tools.Ffmpeg);

        ProcessCommand.Run(new ProcessOptions(
            _drive.Path, _work.Path, "HD", FfmpegFolder: Path.GetDirectoryName(tools.Ffmpeg), ModelFolder: TestModels.None));
        var json = Path.Combine(_elsewhere.Path, "report.json");
        var exit = ReportCommand.Run(new ReportOptions([_work.Path], json), TextWriter.Null);
        if (exit != 0)
        {
            throw new InvalidOperationException($"report exited with {exit}.");
        }

        Report = JsonNode.Parse(await File.ReadAllTextAsync(json))!;

        var upload = await UploadCommand.RunAsync(new UploadOptions(_work.Path, UploadDestination.Local), _catalog.Store);
        if (upload.Problems.Count > 0)
        {
            throw new InvalidOperationException("upload left problems: " + string.Join("; ", upload.Problems.Select(p => $"{p.Path}: {p.Reason}")));
        }

        await using var db = await _catalog.CreateMigratedDatabaseAsync();
        using var geonames = new TempFolder();
        var sample = GeoNamesSample.Write(geonames);
        await new GeoNamesLoader(db).LoadAsync(
            GeoNamesFiles.ReadCities(sample.Cities), GeoNamesFiles.ReadAdmin1(sample.Admin1), GeoNamesFiles.ReadCountries(sample.Countries), CancellationToken.None);
        Import = await new ManifestImporter(db, _catalog.Store, new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance)
            .ImportOnceAsync(BlobLayout.ManifestsContainer, CancellationToken.None);

        _web = new AcceptanceWeb(_catalog.ConnectionStringFor(db), _catalog.BlobConnectionString, _translator);
        // Builds the web's host (its Program.cs), as a request would, without sending one.
        _ = _web.Services;
    }

    public async Task DisposeAsync()
    {
        if (_web is not null)
        {
            await _web.DisposeAsync();
        }

        await _catalog.DisposeAsync();
    }

    // xUnit calls this after DisposeAsync, so the web is already stopped and only the folders are left.
    public void Dispose()
    {
        _web?.Dispose();
        _asking.Dispose();
        _drive.Dispose();
        _work.Dispose();
        _elsewhere.Dispose();
    }

    /// <summary>
    /// Asks <paramref name="question"/> as the question box does, through the web container's own
    /// <see cref="QuestionService"/> (its library, quota, limits, clock and search options, as Program.cs wires them),
    /// with <paramref name="translator"/> answering in place of the configured one; then counts the query it became.
    /// </summary>
    public async Task<(AskResult Asked, CountResult? Count)> AskAsync(string question, IQuestionTranslator translator)
    {
        // One question at a time: the web's translator is a single stand-in that forwards to this one.
        await _asking.WaitAsync();
        try
        {
            _translator.Current = translator;
            using var scope = _web!.Services.CreateScope();
            var services = scope.ServiceProvider;
            var person = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, Person)], "acceptance"));
            var asked = await services.GetRequiredService<QuestionService>().AskAsync(question, person);
            var count = asked is { Outcome: AskOutcome.Answered, Query: { Aggregate: not null } query }
                ? await services.GetRequiredService<ILibraryQueries>().CountAsync(query)
                : null;
            return (asked, count);
        }
        finally
        {
            _translator.Current = null;
            _asking.Release();
        }
    }

    /// <summary>Counts <paramref name="dsl"/> with the web's library queries, with no question in between.</summary>
    public async Task<CountResult> CountAsync(string dsl)
    {
        if (!SearchQueryJson.TryParse(dsl, out var query, out var error))
        {
            throw new ArgumentException(error, nameof(dsl));
        }

        using var scope = _web!.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ILibraryQueries>().CountAsync(query)
            ?? throw new InvalidOperationException("Not a count: " + dsl);
    }

    /// <summary>
    /// The web as Program.cs builds it, on the test catalog and Azurite, with the test's translator as its
    /// <see cref="IQuestionTranslator"/>. The entry type is any web type: the worker has a <c>Program</c> too.
    /// </summary>
    private sealed class AcceptanceWeb(string catalog, string blobs, IQuestionTranslator translator) : WebApplicationFactory<QuestionService>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
            .UseEnvironment("Development")
            .UseSetting("ConnectionStrings:catalog", catalog)
            .UseSetting("ConnectionStrings:blobs", blobs)
            .UseSetting("Access:Emails", Person)
            .UseSetting("Search:TimeZone", "Europe/Lisbon")
            .UseSetting("Search:ModelFolder", TestModels.None)
            // Development loads the developer's user secrets; these settings override them (and environment
            // variables), so neither a key nor a vault there reaches Anthropic or Azure. Settings, not an in-memory
            // source, because Program.cs reads the vault's connection before the app configuration is applied.
            .UseSetting("Anthropic:ApiKey", string.Empty)
            .UseSetting($"ConnectionStrings:{AzureSetup.VaultConnectionName}", string.Empty)
            .UseSetting(AzureSetup.KeyUriKey, string.Empty)
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IQuestionTranslator>();
                services.AddSingleton(translator);
            });
    }

    /// <summary>The web's translator: forwards to the one the current question was asked with.</summary>
    private sealed class ForwardingTranslator : IQuestionTranslator
    {
        public IQuestionTranslator? Current { get; set; }

        public bool IsConfigured => Current?.IsConfigured ?? false;

        public Task<QuestionTranslation> TranslateAsync(string question, QuestionContext context, CancellationToken cancellationToken = default) =>
            (Current ?? throw new InvalidOperationException("No translator for this question.")).TranslateAsync(question, context, cancellationToken);
    }
}
