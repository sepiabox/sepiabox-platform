using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The occasions in the catalog: the <c>eventIds</c> filter, counting by it and the occasions pages' queries (S-023).</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class OccasionsCatalogTests(CatalogFixture catalog)
{
    private static readonly FixedTime s_clock = new(new DateTimeOffset(2026, 10, 19, 9, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task The_event_ids_filter_finds_the_assets_in_any_of_the_occasions()
    {
        await using var db = await CatalogWithOccasionsAsync();

        Assert.Equal([2, 1], await IdsAsync(db, new SearchFilters { EventIds = [1] }));
        Assert.Equal([3, 2, 1], await IdsAsync(db, new SearchFilters { EventIds = [1, 2] }));
        Assert.Empty(await IdsAsync(db, new SearchFilters { EventIds = [999] }));
    }

    [Fact]
    public async Task The_event_ids_filter_combines_with_the_others_and_leaves_out_undated_assets()
    {
        await using var db = await CatalogWithOccasionsAsync();

        Assert.Equal([2], await IdsAsync(db, new SearchFilters { EventIds = [1, 2], Kinds = ["video"] }));
        // Undated assets are in no occasion, so no occasion finds them and "undated only" finds no one in one.
        Assert.Empty(await IdsAsync(db, new SearchFilters { EventIds = [1, 2], UndatedOnly = true }));
        Assert.Equal([4], await IdsAsync(db, new SearchFilters { UndatedOnly = true }));
    }

    [Fact]
    public async Task Counts_the_assets_in_an_occasion()
    {
        await using var db = await CatalogWithOccasionsAsync();

        var count = await new SearchService(db, s_clock).CountAsync(new SearchQuery
        {
            Filters = new SearchFilters { EventIds = [1] },
            Aggregate = new SearchAggregate(),
        });

        Assert.Equal(2, count!.Total);
    }

    [Fact]
    public async Task Lists_the_occasions_newest_first_with_up_to_three_thumbnails_in_capture_order()
    {
        await using var db = await CatalogWithOccasionsAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var page = await queries.GetOccasionsPageAsync(1);

        Assert.Equal([2L, 1L], page.Items.Select(o => o.Id));
        Assert.False(page.HasMore);
        var first = page.Items[1];
        Assert.Equal((new DateTime(2024, 12, 14, 9, 0, 0), new DateTime(2024, 12, 14, 10, 0, 0), "Lisbon", 2), (first.StartsAt, first.EndsAt, first.PlaceLabel, first.AssetCount));
        Assert.Equal([Ids.Of(1), Ids.Of(2)], first.Thumbnails);
        Assert.Equal([Ids.Of(3)], page.Items[0].Thumbnails);
    }

    [Fact]
    public async Task A_card_has_only_the_first_three_assets_that_have_a_thumbnail()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var start = new DateTime(2024, 12, 14, 9, 0, 0);
        // The first asset has no thumbnail; five more follow.
        db.Assets.Add(CatalogData.Photo(Ids.Of(1), start, withDerivatives: false));
        db.Assets.AddRange(Enumerable.Range(2, 5).Select(n => CatalogData.Photo(Ids.Of(n), start.AddMinutes(n))));
        db.Occasions.Add(new Occasion { Id = 1, StartsAt = start, EndsAt = start.AddMinutes(6), AssetCount = 6 });
        await db.SaveChangesAsync();
        db.OccasionAssets.AddRange(Enumerable.Range(1, 6).Select(n => new OccasionAsset { EventId = 1, AssetId = Ids.Of(n) }));
        await db.SaveChangesAsync();

        var page = await new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db))).GetOccasionsPageAsync(1);

        Assert.Equal([Ids.Of(2), Ids.Of(3), Ids.Of(4)], page.Items.Single().Thumbnails);
    }

    [Fact]
    public async Task Pages_hold_24_occasions_and_a_page_beyond_the_end_is_empty()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Occasions.AddRange(Enumerable.Range(1, 25).Select(n => new Occasion
        {
            StartsAt = new DateTime(2020, 1, 1).AddDays(n),
            EndsAt = new DateTime(2020, 1, 1).AddDays(n),
            AssetCount = 1,
        }));
        await db.SaveChangesAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var first = await queries.GetOccasionsPageAsync(1);
        var second = await queries.GetOccasionsPageAsync(2);
        var beyond = await queries.GetOccasionsPageAsync(int.MaxValue);

        Assert.Equal((LibraryQueries.OccasionsPageSize, true), (first.Items.Count, first.HasMore));
        Assert.Equal((1, false), (second.Items.Count, second.HasMore));
        Assert.True(first.Items[0].StartsAt > second.Items[0].StartsAt);
        Assert.Empty(beyond.Items);
        Assert.False(beyond.HasMore);
    }

    [Fact]
    public async Task Reads_one_occasion_by_id_and_null_for_one_that_is_not_there()
    {
        await using var db = await CatalogWithOccasionsAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        Assert.Equal("Lisbon", (await queries.GetOccasionAsync(1))!.PlaceLabel);
        Assert.Null(await queries.GetOccasionAsync(99));
    }

    [Fact]
    public async Task Deleting_an_asset_takes_it_out_of_its_occasion()
    {
        await using var db = await CatalogWithOccasionsAsync();

        await db.Assets.Where(a => a.Id == Ids.Of(1)).ExecuteDeleteAsync();

        Assert.Equal([Ids.Of(2), Ids.Of(3)], await db.OccasionAssets.Select(l => l.AssetId).OrderBy(i => i).ToListAsync());
    }

    // Assets 1 and 2 (a photo and a video) in occasion 1, asset 3 in occasion 2, and asset 4 undated and in none.
    private async Task<CatalogDbContext> CatalogWithOccasionsAsync()
    {
        var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.AddRange(
            CatalogData.Photo(Ids.Of(1), new DateTime(2024, 12, 14, 9, 0, 0)),
            CatalogData.Video(Ids.Of(2), new DateTime(2024, 12, 14, 10, 0, 0)),
            CatalogData.Photo(Ids.Of(3), new DateTime(2025, 3, 2, 12, 0, 0)),
            CatalogData.Photo(Ids.Of(4), null));
        db.Occasions.AddRange(
            new Occasion { Id = 1, StartsAt = new DateTime(2024, 12, 14, 9, 0, 0), EndsAt = new DateTime(2024, 12, 14, 10, 0, 0), PlaceLabel = "Lisbon", AssetCount = 2 },
            new Occasion { Id = 2, StartsAt = new DateTime(2025, 3, 2, 12, 0, 0), EndsAt = new DateTime(2025, 3, 2, 12, 0, 0), AssetCount = 1 });
        await db.SaveChangesAsync();
        db.OccasionAssets.AddRange(
            new OccasionAsset { EventId = 1, AssetId = Ids.Of(1) },
            new OccasionAsset { EventId = 1, AssetId = Ids.Of(2) },
            new OccasionAsset { EventId = 2, AssetId = Ids.Of(3) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static async Task<List<int>> IdsAsync(CatalogDbContext db, SearchFilters filters)
    {
        var result = await new SearchService(db, s_clock).ExecuteAsync(new SearchQuery { Filters = filters });
        return [.. result.Hits.Select(h => int.Parse(h.Id[^8..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture))];
    }
}
