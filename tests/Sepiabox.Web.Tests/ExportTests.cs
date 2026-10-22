using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Catalog;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>Asking for an export on a catalog and getting a one-hour link to it (S-040).</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class ExportRequestsTests(CatalogFixture catalog)
{
    private static readonly FixedTime s_clock = new(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Keeps_a_zip_seven_days_as_the_lifecycle_rule_does()
    {
        // AzureInfrastructure.ExportRetentionDays (the AppHost, which cannot see the catalog) is pinned to 7 in its own test.
        Assert.Equal(7, ExportRequest.RetentionDays);
    }

    [Fact]
    public async Task Records_a_request_for_the_person_who_asked_and_refuses_a_second_while_it_waits()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var exports = Exports(catalog, db, new RecordingLinks());

        Assert.True(await exports.RequestAsync("Ana@Example.com", includeFaceData: true));
        Assert.False(await exports.RequestAsync("ana@example.com", includeFaceData: false));

        var request = Assert.Single(await exports.RecentAsync());
        Assert.Equal(("ana@example.com", true, ExportStatuses.Requested), (request.RequestedBy, request.IncludeFaceData, request.Status));
        Assert.Equal(s_clock.GetUtcNow(), request.RequestedAt);
    }

    [Fact]
    public async Task Two_requests_at_the_same_moment_make_only_one_because_the_database_allows_one_active()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var exports = Exports(catalog, db, new RecordingLinks());

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => exports.RequestAsync("ana@example.com", false)));

        Assert.Single(results, r => r);
        Assert.Single(await exports.RecentAsync());
    }

    [Fact]
    public async Task Gives_no_link_to_a_zip_older_than_the_retention_the_lifecycle_rule_deletes()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var links = new RecordingLinks();
        var exports = Exports(catalog, db, links);
        var written = s_clock.GetUtcNow().AddDays(-ExportRequest.RetentionDays).AddMinutes(1);
        db.ExportRequests.AddRange(
            new ExportRequest { RequestedBy = "a@b.c", RequestedAt = written, FinishedAt = written, Status = ExportStatuses.Done, BlobName = "kept.zip" },
            new ExportRequest { RequestedBy = "a@b.c", RequestedAt = written.AddMinutes(-2), FinishedAt = written.AddMinutes(-2), Status = ExportStatuses.Done, BlobName = "old.zip" });
        await db.SaveChangesAsync();

        Assert.NotNull(await exports.DownloadAsync(db.ExportRequests.Single(e => e.BlobName == "kept.zip").Id));
        Assert.Null(await exports.DownloadAsync(db.ExportRequests.Single(e => e.BlobName == "old.zip").Id));
        Assert.Equal(["kept.zip"], links.Exports.Select(e => e.Blob));
    }

    [Fact]
    public async Task Accepts_a_new_request_once_the_earlier_one_has_ended()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var exports = Exports(catalog, db, new RecordingLinks());
        await exports.RequestAsync("ana@example.com", false);
        db.ExportRequests.Single().Status = ExportStatuses.Failed;
        await db.SaveChangesAsync();

        Assert.True(await exports.RequestAsync("ana@example.com", false));
        Assert.Equal(2, (await exports.RecentAsync()).Count);
    }

    [Fact]
    public async Task Links_only_a_finished_export_and_names_the_file_by_the_day_it_was_asked()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var links = new RecordingLinks();
        var exports = Exports(catalog, db, links);
        db.ExportRequests.AddRange(
            new ExportRequest { RequestedAt = s_clock.GetUtcNow(), RequestedBy = "a@b.c", Status = ExportStatuses.Done, BlobName = "2026-10-22-1.zip" },
            new ExportRequest { RequestedAt = s_clock.GetUtcNow(), RequestedBy = "a@b.c", Status = ExportStatuses.Running });
        await db.SaveChangesAsync();
        var done = db.ExportRequests.Single(e => e.Status == ExportStatuses.Done).Id;
        var running = db.ExportRequests.Single(e => e.Status == ExportStatuses.Running).Id;

        Assert.Equal("https://storage.test/exports/2026-10-22-1.zip", (await exports.DownloadAsync(done))!.AbsoluteUri);
        Assert.Equal(("2026-10-22-1.zip", "sepiabox-export-2026-10-22.zip"), links.Exports.Single());
        Assert.Null(await exports.DownloadAsync(running));
        Assert.Null(await exports.DownloadAsync(999));
    }

    private static ExportRequests Exports(CatalogFixture catalog, CatalogDbContext db, ISasUrlFactory links) =>
        new(new TestContexts(catalog.ConnectionStringFor(db)), links, s_clock);

    private sealed class RecordingLinks : ISasUrlFactory
    {
        public List<(string Blob, string FileName)> Exports { get; } = [];

        public Task<Uri> ForDerivativeAsync(string blobPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Uri> ForVideoAsync(string blobPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Uri> ForOriginalAsync(string assetId, OriginalFile file, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Uri> ForExportAsync(string blobName, string fileName, CancellationToken cancellationToken = default)
        {
            Exports.Add((blobName, fileName));
            return Task.FromResult(new Uri($"https://storage.test/exports/{blobName}"));
        }
    }
}

/// <summary>The export page and its download endpoint (S-040), in bUnit and directly.</summary>
public sealed class ExportPageTests : LocalizedContext
{
    private readonly FakeExports _exports = new();

    public ExportPageTests() => Services.AddSingleton<IExportRequests>(_exports);

    [Fact]
    public void Offers_the_request_with_face_data_off_and_lists_nothing_when_there_are_no_requests()
    {
        var page = Render<ExportData>();

        Assert.Equal("Export all data", page.Find("h1").TextContent);
        Assert.Equal("post", page.Find("form.export-request").GetAttribute("method"));
        Assert.Null(page.Find("input[name=IncludeFaceData]").GetAttribute("checked"));
        Assert.Contains("Include face data", page.Find("form.export-request label").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("ul.exports"));
    }

    [Fact]
    public void Lists_each_request_with_a_download_only_for_a_finished_one()
    {
        _exports.Recent =
        [
            new ExportRequest { RequestedBy = "a@b.c", Id = 3, RequestedAt = DateTimeOffset.UnixEpoch, Status = ExportStatuses.Requested },
            new ExportRequest { RequestedBy = "a@b.c", Id = 2, RequestedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow, Status = ExportStatuses.Done, BlobName = "x.zip", Assets = 12, SizeBytes = 2048, IncludeFaceData = true },
            new ExportRequest { RequestedBy = "a@b.c", Id = 1, RequestedAt = DateTimeOffset.UnixEpoch, Status = ExportStatuses.Failed, Error = "The export could not be written (IOException). Ask for it again." },
        ];

        var page = Render<ExportData>();

        var items = page.FindAll("ul.exports li");
        Assert.Equal(3, items.Count);
        Assert.Empty(items[0].QuerySelectorAll("a"));
        Assert.Equal("settings/export/2/download", items[1].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("12 items", items[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("with face data", items[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Ask for it again", items[2].TextContent, StringComparison.Ordinal);
        Assert.Empty(items[2].QuerySelectorAll("a"));
    }

    [Fact]
    public void Says_how_long_each_zip_and_its_links_last_and_offers_no_download_for_a_deleted_one()
    {
        var now = DateTimeOffset.UtcNow;
        Services.AddSingleton<TimeProvider>(new FixedTime(now));
        _exports.Recent =
        [
            new ExportRequest { RequestedBy = "a@b.c", Id = 4, RequestedAt = now.AddHours(-1), FinishedAt = now.AddHours(-1), Status = ExportStatuses.Done, BlobName = "a.zip", Assets = 3, SizeBytes = 100 },
            new ExportRequest { RequestedBy = "a@b.c", Id = 3, RequestedAt = now.AddDays(-2), FinishedAt = now.AddDays(-2), Status = ExportStatuses.Done, BlobName = "b.zip", Assets = 3, SizeBytes = 100 },
            new ExportRequest { RequestedBy = "a@b.c", Id = 2, RequestedAt = now.AddDays(-8), FinishedAt = now.AddDays(-8), Status = ExportStatuses.Done, BlobName = "c.zip", Assets = 3, SizeBytes = 100 },
            new ExportRequest { RequestedBy = "a@b.c", Id = 1, RequestedAt = now.AddDays(-1), FinishedAt = now.AddDays(-1), Status = ExportStatuses.Done, BlobName = null, IncludeFaceData = true, Assets = 3, SizeBytes = 100 },
        ];

        var page = Render<ExportData>();

        var items = page.FindAll("ul.exports li");
        Assert.Equal("settings/export/4/download", items[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("Kept until", items[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Links to the originals inside work until", items[0].TextContent, StringComparison.Ordinal);
        // After 24 hours the links inside are dead, so the page does not promise them; the zip is still kept.
        Assert.Contains("Kept until", items[1].TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Links to the originals", items[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Deleted after 7 days", items[2].TextContent, StringComparison.Ordinal);
        Assert.Empty(items[2].QuerySelectorAll("a"));
        Assert.Contains("Deleted: face recognition was turned off", items[3].TextContent, StringComparison.Ordinal);
        Assert.Empty(items[3].QuerySelectorAll("a"));
        Assert.Contains("deleted after 7 days", page.Find("p.small").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_so_in_brazilian_portuguese_and_shows_only_the_notes_it_knows()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("settings/export?note=requested");

        var page = Render<ExportData>();

        Assert.Equal("Exportar todos os dados", page.Find("h1").TextContent);
        Assert.StartsWith("A exportação foi pedida", page.Find(".note").TextContent, StringComparison.Ordinal);

        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("settings/export?note=%3Cb%3Ex");
        Assert.Empty(Render<ExportData>().FindAll(".note"));
    }

    [Fact]
    public async Task Redirects_the_download_to_the_link_and_answers_not_found_for_none()
    {
        _exports.Link = new Uri("https://storage.test/exports/a.zip?sig=x");
        var http = new DefaultHttpContext();

        var found = await ExportEndpoints.DownloadAsync(7, http, _exports);
        var missing = await ExportEndpoints.DownloadAsync(8, http, new FakeExports());

        Assert.Equal("https://storage.test/exports/a.zip?sig=x", Assert.IsType<RedirectHttpResult>(found).Url);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(missing);
        Assert.Equal(7, _exports.Downloaded.Single());
    }

    private sealed class FakeExports : IExportRequests
    {
        public IReadOnlyList<ExportRequest> Recent { get; set; } = [];

        public Uri? Link { get; set; }

        public List<long> Downloaded { get; } = [];

        public Task<IReadOnlyList<ExportRequest>> RecentAsync(CancellationToken cancellationToken = default) => Task.FromResult(Recent);

        public Task<bool> RequestAsync(string email, bool includeFaceData, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<Uri?> DownloadAsync(long id, CancellationToken cancellationToken = default)
        {
            Downloaded.Add(id);
            return Task.FromResult(Link);
        }
    }
}
