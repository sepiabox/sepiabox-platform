using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;
using Sepiabox.Web.Sources;

namespace Sepiabox.Web.Tests;

/// <summary>"Open original" for a Google Drive source (S-038, D-115): the link, the detail page and the endpoint.</summary>
public sealed class DriveOriginalLinkTests : LocalizedContext
{
    private static readonly AssetSourceDetail s_drive = new("google-drive", "ana@example.com", "Fotos/Praia 2019.jpg", "https://drive.google.com/file/d/1/view", 7);

    [Theory]
    [InlineData("google-drive", 7, false, false, true)]
    [InlineData("google-drive", 7, false, true, false)]
    [InlineData("google-drive", 7, true, false, false)]
    [InlineData("google-drive", 0, false, false, false)]
    [InlineData("onedrive", 7, false, false, false)]
    [InlineData("local", 7, false, false, false)]
    public void Offers_the_original_through_the_server_only_for_a_drive_file_that_is_there_and_not_stored(
        string type, long id, bool stored, bool removed, bool offered)
    {
        var href = SourceLinks.OriginalHref(Ids.Of(1), new AssetSourceDetail(type, "x", "a.jpg", null, id, removed), stored);

        Assert.Equal(offered ? $"photos/{Ids.Of(1)}/sources/{id}/original" : null, href);
    }

    [Fact]
    public void The_detail_page_shows_open_original_for_a_drive_source_whose_original_is_not_stored()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(1)) with { Sources = [s_drive], OriginalStored = false });

        var link = page.Find("ul.sources a[href$='/original']");
        Assert.Equal($"photos/{Ids.Of(1)}/sources/7/original", link.GetAttribute("href"));
        Assert.Equal("Open original", link.TextContent);
        // Not followed by the enhanced navigation, which would fetch the file itself.
        Assert.Equal("false", link.GetAttribute("data-enhance-nav"));
        // The address Drive gave stays as the source's own link.
        Assert.Contains(page.FindAll("ul.sources a"), a => a.GetAttribute("href") == "https://drive.google.com/file/d/1/view");
    }

    [Fact]
    public void The_detail_page_says_so_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);

        var page = RenderDetail(Details.Photo(Ids.Of(1)) with { Sources = [s_drive], OriginalStored = false });

        Assert.Equal("Abrir original", page.Find("ul.sources a[href$='/original']").TextContent);
        Assert.Contains("pode não terminar", page.Find("ul.sources .original-hint").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_detail_page_warns_that_the_download_goes_through_the_server_in_one_piece()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(1)) with { Sources = [s_drive], OriginalStored = false });

        Assert.Contains("through Sepiabox, in one piece", page.Find("ul.sources .original-hint").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_source_the_sync_found_gone_is_marked_and_has_no_open_original()
    {
        var page = RenderDetail(Details.Photo(Ids.Of(1)) with { Sources = [s_drive with { Removed = true }], OriginalStored = false });

        Assert.Empty(page.FindAll("ul.sources a[href$='/original']"));
        Assert.Equal("No longer at the source", page.Find("ul.sources .source-removed").TextContent);
    }

    private IRenderedComponent<PhotoDetail> RenderDetail(AssetDetail asset)
    {
        var library = new FakeLibrary();
        library.Assets[asset.Id] = asset;
        Services.AddSingleton<ILibraryQueries>(library);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}");
        return Render<PhotoDetail>(p => p.Add(d => d.Id, asset.Id));
    }
}

/// <summary>The endpoint's answers, with a fake of the service behind it.</summary>
public sealed class DriveOriginalEndpointTests
{
    [Fact]
    public async Task Streams_the_file_under_its_name_without_letting_a_cache_keep_it()
    {
        var http = new DefaultHttpContext();
        var originals = new FakeOriginals(DriveOriginalOutcome.Opened, new DriveFileDownload(new MemoryStream([1, 2, 3]), "Praia 2019.jpg", "image/jpeg", 3));

        var result = await DriveOriginalEndpoint.OpenAsync(Ids.Of(1), 7, http, originals);

        var file = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.FileStreamHttpResult>(result);
        Assert.Equal(("image/jpeg", "Praia 2019.jpg"), (file.ContentType, file.FileDownloadName));
        Assert.Equal("private, no-store", http.Response.Headers.CacheControl.ToString());
        Assert.Equal((Ids.Of(1), 7L), originals.Asked.Single());
    }

    [Fact]
    public async Task Sends_the_visitor_to_the_sources_page_when_the_account_must_be_connected_again()
    {
        var result = await DriveOriginalEndpoint.OpenAsync(Ids.Of(1), 7, new DefaultHttpContext(), new FakeOriginals(DriveOriginalOutcome.Reconnect, null));

        Assert.Equal("/sources?note=reconnect", Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult>(result).Url);
    }

    [Theory]
    [InlineData(DriveOriginalOutcome.NotFound, 404)]
    [InlineData(DriveOriginalOutcome.Failed, 502)]
    public async Task Answers_not_found_or_bad_gateway(DriveOriginalOutcome outcome, int status)
    {
        var result = await DriveOriginalEndpoint.OpenAsync(Ids.Of(1), 7, new DefaultHttpContext(), new FakeOriginals(outcome, null));

        Assert.Equal(status, (result as IStatusCodeHttpResult)!.StatusCode);
    }

    [Fact]
    public async Task Asks_nothing_for_an_id_that_is_not_an_asset_s()
    {
        var originals = new FakeOriginals(DriveOriginalOutcome.Opened, null);

        var result = await DriveOriginalEndpoint.OpenAsync("../../etc/passwd", 7, new DefaultHttpContext(), originals);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(result);
        Assert.Empty(originals.Asked);
    }

    private sealed class FakeOriginals(DriveOriginalOutcome outcome, DriveFileDownload? file) : IDriveOriginals
    {
        public List<(string Asset, long Source)> Asked { get; } = [];

        public Task<(DriveOriginalOutcome Outcome, DriveFileDownload? File)> OpenAsync(string assetId, long sourceId, CancellationToken cancellationToken = default)
        {
            Asked.Add((assetId, sourceId));
            return Task.FromResult((outcome, file));
        }
    }
}

/// <summary>The service on a catalog: finds the file from the source the page listed, and the account's token.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class DriveOriginalsCatalogTests(CatalogFixture catalog)
{
    private const string Account = "ana@example.com";
    private static readonly IDataProtectionProvider s_keys = new EphemeralDataProtectionProvider();

    [Fact]
    public async Task Opens_the_file_the_sync_recorded_for_the_source_with_the_accounts_plain_token()
    {
        await using var db = await SeedAsync();
        var drive = new FakeDrive();
        drive.Files["file-1"] = Encoding.UTF8.GetBytes("jpeg bytes");

        var (outcome, file) = await Originals(db, drive).OpenAsync(Ids.Of(1), await SourceIdAsync(db, "Fotos/Praia 2019.jpg"));

        Assert.Equal(DriveOriginalOutcome.Opened, outcome);
        await using (file)
        {
            using var reader = new StreamReader(file!.Content);
            Assert.Equal("jpeg bytes", await reader.ReadToEndAsync());
        }

        Assert.Equal("1//refresh", drive.TokensUsed.Single());
    }

    [Fact]
    public async Task Finds_nothing_for_another_asset_a_removed_source_a_source_of_another_kind_or_a_file_the_drive_lost()
    {
        await using var db = await SeedAsync();
        var drive = new FakeDrive();
        var originals = Originals(db, drive);
        var local = await SourceIdAsync(db, "2019/praia.jpg");
        var gone = await SourceIdAsync(db, "Fotos/Gone.jpg");
        var missing = await SourceIdAsync(db, "Fotos/Praia 2019.jpg");

        Assert.Equal(DriveOriginalOutcome.NotFound, (await originals.OpenAsync(Ids.Of(2), missing)).Outcome);
        Assert.Equal(DriveOriginalOutcome.NotFound, (await originals.OpenAsync(Ids.Of(1), local)).Outcome);
        Assert.Equal(DriveOriginalOutcome.NotFound, (await originals.OpenAsync(Ids.Of(1), gone)).Outcome);
        // The sync recorded the file, but Drive no longer has it.
        Assert.Equal(DriveOriginalOutcome.NotFound, (await originals.OpenAsync(Ids.Of(1), missing)).Outcome);
        Assert.Equal(DriveOriginalOutcome.NotFound, (await originals.OpenAsync(Ids.Of(1), 999_999)).Outcome);
    }

    [Fact]
    public async Task Two_files_of_one_name_in_one_folder_open_the_one_the_source_describes_and_an_ambiguous_one_is_not_found()
    {
        await using var db = await SeedAsync();
        var account = await db.ConnectedSources.AsNoTracking().SingleAsync();
        var when = new DateTimeOffset(2020, 5, 1, 10, 0, 0, TimeSpan.Zero);
        // Another file at the same path, modified at another time, and the asset's own file with the time its source has.
        db.AssetSources.Add(new AssetSource { AssetId = Ids.Of(2), Type = "google-drive", Name = Account, Path = "Fotos/Dupe.jpg", ModifiedAt = when });
        db.DriveFiles.AddRange(
            new DriveFile { ConnectedSourceId = account.Id, FileId = "dupe-a", Path = "Fotos/Dupe.jpg", ModifiedAt = when },
            new DriveFile { ConnectedSourceId = account.Id, FileId = "dupe-b", Path = "Fotos/Dupe.jpg", ModifiedAt = when.AddDays(1) });
        await db.SaveChangesAsync();
        var drive = new FakeDrive();
        drive.Files["dupe-a"] = [1];
        drive.Files["dupe-b"] = [2];
        var dupe = await SourceIdAsync(db, "Fotos/Dupe.jpg");

        var (outcome, file) = await Originals(db, drive).OpenAsync(Ids.Of(2), dupe);

        Assert.Equal(DriveOriginalOutcome.Opened, outcome);
        await using (file)
        {
            Assert.Equal(1, file!.Content.ReadByte());
        }

        // Both at the source's time: nothing says which, so it is not found, and nothing throws.
        db.DriveFiles.Single(f => f.FileId == "dupe-b").ModifiedAt = when;
        await db.SaveChangesAsync();
        Assert.Equal(DriveOriginalOutcome.NotFound, (await Originals(db, drive).OpenAsync(Ids.Of(2), dupe)).Outcome);
    }

    [Fact]
    public async Task A_token_google_refuses_or_a_key_ring_that_cannot_read_it_makes_the_account_reconnect()
    {
        await using var db = await SeedAsync();
        var drive = new FakeDrive { Failure = () => new DriveAuthorizationException("invalid_grant") };
        var source = await SourceIdAsync(db, "Fotos/Praia 2019.jpg");

        var refused = await Originals(db, drive).OpenAsync(Ids.Of(1), source);

        Assert.Equal(DriveOriginalOutcome.Reconnect, refused.Outcome);
        Assert.Equal(ConnectedSourceStatuses.Reconnect, (await db.ConnectedSources.AsNoTracking().SingleAsync()).Status);

        db.ChangeTracker.Clear();
        (await db.ConnectedSources.SingleAsync()).Status = ConnectedSourceStatuses.Connected;
        await db.SaveChangesAsync();
        var otherRing = new DriveOriginals(new TestContexts(catalog.ConnectionStringFor(db)), new DriveTokenProtector(new EphemeralDataProtectionProvider()), new FakeDrive(), NullLogger<DriveOriginals>.Instance);

        Assert.Equal(DriveOriginalOutcome.Reconnect, (await otherRing.OpenAsync(Ids.Of(1), source)).Outcome);
    }

    [Fact]
    public async Task Any_other_failure_is_reported_and_leaves_the_account_alone()
    {
        await using var db = await SeedAsync();
        var drive = new FakeDrive { Failure = () => new HttpRequestException("down") };

        var result = await Originals(db, drive).OpenAsync(Ids.Of(1), await SourceIdAsync(db, "Fotos/Praia 2019.jpg"));

        Assert.Equal(DriveOriginalOutcome.Failed, result.Outcome);
        Assert.Equal(ConnectedSourceStatuses.Connected, (await db.ConnectedSources.AsNoTracking().SingleAsync()).Status);
    }

    private DriveOriginals Originals(CatalogDbContext db, FakeDrive drive) =>
        new(new TestContexts(catalog.ConnectionStringFor(db)), new DriveTokenProtector(s_keys), drive, NullLogger<DriveOriginals>.Instance);

    private static Task<long> SourceIdAsync(CatalogDbContext db, string path) =>
        db.AssetSources.AsNoTracking().Where(s => s.Path == path).Select(s => s.Id).SingleAsync();

    // Asset 1 has a Drive source (with its file recorded), a removed Drive source and a local one; asset 2 has none of them.
    private async Task<CatalogDbContext> SeedAsync()
    {
        var db = await catalog.CreateMigratedDatabaseAsync();
        var asset = CatalogData.Photo(Ids.Of(1), new DateTime(2019, 7, 14, 9, 0, 0));
        asset.Sources =
        [
            new AssetSource { AssetId = Ids.Of(1), Type = "google-drive", Name = Account, Path = "Fotos/Praia 2019.jpg", ModifiedAt = DateTimeOffset.UtcNow },
            new AssetSource { AssetId = Ids.Of(1), Type = "google-drive", Name = Account, Path = "Fotos/Gone.jpg", ModifiedAt = DateTimeOffset.UtcNow, RemovedAt = DateTimeOffset.UtcNow },
            new AssetSource { AssetId = Ids.Of(1), Type = "local", Name = "HD", Path = "2019/praia.jpg", ModifiedAt = DateTimeOffset.UtcNow },
        ];
        db.Assets.AddRange(asset, CatalogData.Photo(Ids.Of(2), null));
        var account = new ConnectedSource
        {
            Type = ConnectedSourceTypes.GoogleDrive,
            AccountEmail = Account,
            EncryptedRefreshToken = new DriveTokenProtector(s_keys).Protect("1//refresh"),
            Status = ConnectedSourceStatuses.Connected,
            ConnectedAt = DateTimeOffset.UtcNow,
        };
        db.ConnectedSources.Add(account);
        await db.SaveChangesAsync();
        db.DriveFiles.AddRange(
            new DriveFile { ConnectedSourceId = account.Id, FileId = "file-1", Path = "Fotos/Praia 2019.jpg", ModifiedAt = DateTimeOffset.UtcNow },
            new DriveFile { ConnectedSourceId = account.Id, FileId = "file-gone", Path = "Fotos/Gone.jpg", ModifiedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }
}
