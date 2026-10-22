using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>
/// Bursts, blurry photos and setting photos aside, on a real PostgreSQL (S-027, D-098): the lists, the one action, and
/// what a hidden photo is left out of. Nothing is deleted.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed partial class OrganizeTests(CatalogFixture catalog)
{
    private static readonly FixedTime s_clock = new(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));
    private static readonly DateTime s_shot = new(2025, 3, 1, 12, 0, 0);

    [Fact]
    public async Task A_hidden_photo_is_not_in_a_search_a_count_or_the_timeline_but_comes_back_with_include_hidden()
    {
        await using var db = await LibraryAsync();
        Assert.Equal(1, await Organization(db).HideAsync([Ids.Of(2)]));

        var found = await IdsAsync(db, new SearchFilters());
        var withHidden = await IdsAsync(db, new SearchFilters { IncludeHidden = true });
        var count = await Search(db).CountAsync(new SearchQuery { Aggregate = new SearchAggregate() });
        var timeline = await new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db))).GetTimelinePageAsync(1);

        Assert.DoesNotContain(Ids.Of(2), found);
        Assert.Contains(Ids.Of(2), withHidden);
        Assert.Equal(found.Count, count!.Total);
        Assert.DoesNotContain(Ids.Of(2), timeline.Items.Select(i => i.Id));
        Assert.Equal(1, await db.Assets.CountAsync(a => a.Id == Ids.Of(2)));
    }

    [Fact]
    public async Task The_quality_filter_matches_measured_photos_against_the_threshold_and_never_the_unmeasured()
    {
        await using var db = await LibraryAsync();

        var blurry = await IdsAsync(db, new SearchFilters { Quality = new QualityFilter { Blurry = true } });
        var sharp = await IdsAsync(db, new SearchFilters { Quality = new QualityFilter { Blurry = false } });

        Assert.Equal([Ids.Of(4)], blurry);
        Assert.DoesNotContain(Ids.Of(5), blurry.Concat(sharp));
        Assert.DoesNotContain(Ids.Of(4), sharp);
        Assert.Contains(Ids.Of(1), sharp);
    }

    [Fact]
    public async Task The_threshold_comes_from_the_options()
    {
        await using var db = await LibraryAsync();
        var strict = new SearchService(db, s_clock, new SearchOptions { BlurryBelow = 500 });

        var blurry = (await strict.ExecuteAsync(new SearchQuery { Filters = new SearchFilters { Quality = new QualityFilter { Blurry = true } } })).Hits.Select(h => h.Id);

        Assert.Contains(Ids.Of(1), blurry);
        Assert.Equal(500, new Organization(new TestContexts(catalog.ConnectionStringFor(db)), new SearchOptions { BlurryBelow = 500 }).BlurryBelow);
    }

    [Fact]
    public async Task Lists_the_burst_of_three_photos_a_second_apart_with_the_sharpest_to_keep_and_leaves_the_rest()
    {
        await using var db = await LibraryAsync();

        var page = await Organization(db).GetBurstsAsync(1);

        var burst = Assert.Single(page.Items);
        Assert.Equal([Ids.Of(1), Ids.Of(2), Ids.Of(3)], burst.Items.Select(i => i.Item.Id));
        Assert.Equal(Ids.Of(2), burst.KeepId);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task A_photo_set_aside_leaves_its_burst_and_returns_when_brought_back()
    {
        await using var db = await LibraryAsync();
        var organization = Organization(db);

        await organization.HideAsync([Ids.Of(1), Ids.Of(3)]);
        var afterHide = await organization.GetBurstsAsync(1);
        var hidden = await organization.GetHiddenAsync(1);
        Assert.Equal(2, await organization.UnhideAsync([Ids.Of(1), Ids.Of(3), Ids.Of(1)]));
        var afterBringBack = await organization.GetBurstsAsync(1);

        Assert.Empty(afterHide.Items);
        Assert.Equal([Ids.Of(3), Ids.Of(1)], hidden.Items.Select(i => i.Item.Id));
        Assert.Equal(3, Assert.Single(afterBringBack.Items).Items.Count);
    }

    [Fact]
    public async Task Lists_blurry_photos_blurriest_first_and_not_the_hidden_or_unmeasured()
    {
        await using var db = await LibraryAsync();
        await Organization(db).HideAsync([Ids.Of(6)]);

        var blurry = await Organization(db).GetBlurryAsync(1);

        Assert.Equal([Ids.Of(4)], blurry.Items.Select(i => i.Item.Id));
        Assert.Equal(1, blurry.Total);
    }

    [Fact]
    public async Task Setting_aside_ignores_unknown_and_malformed_ids_and_counts_only_what_changed()
    {
        await using var db = await LibraryAsync();
        var organization = Organization(db);

        Assert.Equal(1, await organization.HideAsync([Ids.Of(1), Ids.Of(99), "../x", "", Ids.Of(1)]));
        Assert.Equal(0, await organization.HideAsync([Ids.Of(1)]));
        Assert.Equal(0, await organization.HideAsync([]));
    }

    [Fact]
    public async Task A_reimport_keeps_a_photo_set_aside_hidden()
    {
        await using var db = await LibraryAsync();
        await Organization(db).HideAsync([Ids.Of(1)]);

        var asset = await db.Assets.SingleAsync(a => a.Id == Ids.Of(1));
        asset.Sharpness = 400;
        asset.DHash = "ffffffffffffffff";
        await db.SaveChangesAsync();

        Assert.True((await db.Assets.AsNoTracking().SingleAsync(a => a.Id == Ids.Of(1))).Hidden);
    }

    [Fact]
    public async Task The_organize_page_lists_the_blurry_photos_and_a_post_sets_the_checked_ones_aside()
    {
        await using var db = await LibraryAsync();
        await using var web = new WebFactory(catalog.ConnectionStringFor(db));
        using var http = web.CreateSignedInClient(followRedirects: false);

        var html = await http.GetStringAsync(new Uri("/organize?view=blurry", UriKind.Relative));
        using var response = await http.PostAsync(new Uri("/organize?view=blurry", UriKind.Relative), new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", Token(html)),
            new("_handler", "organize"),
            new("View", "blurry"),
            new("Action", "hide"),
            new("Ids", Ids.Of(4)),
        ]));

        Assert.Contains($"value=\"{Ids.Of(4)}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"value=\"{Ids.Of(1)}\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/organize?view=blurry&note=hidden&n=1", response.Headers.Location!.PathAndQuery);
        Assert.True((await db.Assets.AsNoTracking().SingleAsync(a => a.Id == Ids.Of(4))).Hidden);
        Assert.Contains("Photos set aside: 1", await http.GetStringAsync(new Uri("/organize?view=hidden&note=hidden&n=1", UriKind.Relative)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changes_only_the_first_500_checked_photos_and_says_so()
    {
        await using var db = await LibraryAsync();
        await using var web = new WebFactory(catalog.ConnectionStringFor(db));
        using var http = web.CreateSignedInClient(followRedirects: false);
        var html = await http.GetStringAsync(new Uri("/organize?view=blurry", UriKind.Relative));
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", Token(html)), new("_handler", "organize"), new("View", "blurry"), new("Action", "hide"),
            new("Ids", Ids.Of(4)),
        };
        fields.AddRange(Enumerable.Range(1000, 600).Select(n => new KeyValuePair<string, string>("Ids", Ids.Of(n))));

        using var response = await http.PostAsync(new Uri("/organize?view=blurry", UriKind.Relative), new FormUrlEncodedContent(fields));

        Assert.Equal("/organize?view=blurry&note=hidden&cap=1&n=1", response.Headers.Location!.PathAndQuery);
        var page = await http.GetStringAsync(new Uri(response.Headers.Location.PathAndQuery.TrimStart('/'), UriKind.Relative));
        Assert.Contains("Only the first 500 checked photos were changed", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaves_hidden_videos_out_of_the_largest_videos_and_hidden_photos_out_of_an_occasion()
    {
        await using var db = await LibraryAsync();
        db.Assets.AddRange(CatalogData.Video(Ids.Of(20), s_shot), CatalogData.Video(Ids.Of(21), s_shot.AddHours(1)));
        var occasion = new Occasion { StartsAt = s_shot, EndsAt = s_shot.AddSeconds(2), AssetCount = 3 };
        db.Occasions.Add(occasion);
        await db.SaveChangesAsync();
        db.OccasionAssets.AddRange(Enumerable.Range(1, 3).Select(n => new OccasionAsset { EventId = occasion.Id, AssetId = Ids.Of(n) }));
        await db.SaveChangesAsync();
        await Organization(db).HideAsync([Ids.Of(20), Ids.Of(1)]);
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var storage = await queries.GetStorageAsync();
        var page = await queries.GetOccasionsPageAsync(1);
        var detail = await queries.GetOccasionAsync(occasion.Id);

        Assert.Equal([Ids.Of(21)], storage.LargestVideos.Select(v => v.Id));
        Assert.DoesNotContain(Ids.Of(1), page.Items.Single().Thumbnails);
        Assert.Equal(2, page.Items.Single().AssetCount);
        Assert.Equal(2, detail!.AssetCount);
    }

    [Fact]
    public async Task Asks_nothing_of_the_catalog_for_an_address_that_is_not_a_view_or_a_page()
    {
        await using var db = await LibraryAsync();
        await using var web = new WebFactory(catalog.ConnectionStringFor(db));
        using var http = web.CreateSignedInClient();

        foreach (var address in new[] { "/organize?view=zzz", "/organize?page=abc", "/organize?view=hidden&page=999999", "/organize?note=<script>&n=x" })
        {
            using var response = await http.GetAsync(new Uri(address, UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("<script>", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_organize_page_needs_a_signed_in_person()
    {
        await using var db = await LibraryAsync();
        await using var web = new WebFactory(catalog.ConnectionStringFor(db));
        using var http = web.CreateAnonymousClient();

        using var response = await http.GetAsync(new Uri("/organize", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex TokenPattern();

    private static string Token(string html) => TokenPattern().Match(html).Groups["token"].Value;

    private static SearchService Search(CatalogDbContext db) => new(db, s_clock);

    private Organization Organization(CatalogDbContext db) => new(new TestContexts(catalog.ConnectionStringFor(db)));

    private static async Task<List<string>> IdsAsync(CatalogDbContext db, SearchFilters filters) =>
        [.. (await Search(db).ExecuteAsync(new SearchQuery { Filters = filters })).Hits.Select(h => h.Id)];

    // Photos 1 to 3: a burst a second apart (2 is the sharpest); 4: far away and blurry; 5: not measured; 6: far away, sharp.
    private async Task<CatalogDbContext> LibraryAsync()
    {
        var db = await catalog.CreateMigratedDatabaseAsync();
        (int N, double Seconds, double? Sharpness, string? Hash)[] rows =
        [
            (1, 0, 300, "0000000000000000"),
            (2, 1, 800, "0000000000000003"),
            (3, 2, 120, "0000000000000007"),
            (4, 3600, 20, "ffffffff00000000"),
            (5, 7200, null, null),
            (6, 10800, 250, "00000000ffffffff"),
        ];
        foreach (var (n, seconds, sharpness, hash) in rows)
        {
            var asset = CatalogData.Photo(Ids.Of(n), s_shot.AddSeconds(seconds));
            asset.Sharpness = sharpness;
            asset.DHash = hash;
            db.Assets.Add(asset);
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }
}
