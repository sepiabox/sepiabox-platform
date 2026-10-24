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
    public async Task Counts_the_occasions_of_each_year_and_of_each_type_over_all_pages_and_for_the_type_chosen()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Occasions.AddRange(Enumerable.Range(1, 30).Select(n => new Occasion
        {
            StartsAt = new DateTime(n <= 25 ? 2024 : 2023, 3, 1).AddDays(n),
            EndsAt = new DateTime(n <= 25 ? 2024 : 2023, 3, 1).AddDays(n),
            AssetCount = 1,
            Type = n % 10 == 0 ? "wedding" : n % 7 == 0 ? "trip" : null,
        }));
        await db.SaveChangesAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var all = await queries.GetOccasionsPageAsync(1);
        var weddings = await queries.GetOccasionsPageAsync(1, "wedding");
        var options = await queries.GetFilterOptionsAsync();

        // 24 on the page, but the headings count them all: 25 in 2024 and 5 in 2023.
        Assert.Equal(24, all.Items.Count);
        Assert.Equal((25, 5), (all.YearCounts[2024], all.YearCounts[2023]));
        // Weddings are 10, 20 and 30; trips 7, 14, 21 and 28.
        Assert.Equal((2, 1), (weddings.YearCounts[2024], weddings.YearCounts[2023]));
        Assert.Equal((3, 4), (options.EventTypeCounts["wedding"], options.EventTypeCounts["trip"]));
        Assert.Equal(["wedding", "trip"], options.EventTypes);
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
    public async Task The_event_types_filter_finds_the_assets_in_occasions_of_any_of_the_types()
    {
        await using var db = await CatalogWithOccasionsAsync();

        Assert.Equal([2, 1], await IdsAsync(db, new SearchFilters { EventTypes = ["wedding"] }));
        Assert.Equal([3, 2, 1], await IdsAsync(db, new SearchFilters { EventTypes = ["wedding", "trip"] }));
        Assert.Empty(await IdsAsync(db, new SearchFilters { EventTypes = ["party"] }));
        // The occasion without a type, and assets in none, never match a type.
        Assert.Equal([3], await IdsAsync(db, new SearchFilters { EventTypes = ["trip"] }));
    }

    [Fact]
    public async Task Counts_the_occasions_of_a_type_each_once_and_groups_them_by_type()
    {
        await using var db = await CatalogWithOccasionsAsync();
        var search = new SearchService(db, s_clock);
        // A third occasion, without a type yet.
        db.Assets.Add(CatalogData.Photo(Ids.Of(5), new DateTime(2023, 1, 1)));
        await db.SaveChangesAsync();
        var untyped = new Occasion { Id = 3, StartsAt = new DateTime(2023, 1, 1), EndsAt = new DateTime(2023, 1, 1), AssetCount = 1 };
        db.Occasions.Add(untyped);
        await db.SaveChangesAsync();
        db.OccasionAssets.Add(new OccasionAsset { EventId = untyped.Id, AssetId = Ids.Of(5) });
        await db.SaveChangesAsync();

        // Two assets of occasion 1 match, and it counts once.
        var weddings = await search.CountAsync(new SearchQuery
        {
            Filters = new SearchFilters { EventTypes = ["wedding"], Years = [2024] },
            Aggregate = new SearchAggregate { Count = SearchCount.Events },
        });
        var byType = await search.CountAsync(new SearchQuery
        {
            Aggregate = new SearchAggregate { Count = SearchCount.Events, GroupBy = SearchGroupBy.EventType },
        });

        Assert.Equal((1, null), (weddings!.Total, weddings.Undated));
        Assert.Equal(3, byType!.Total);
        Assert.Equal([("trip", 1), ("wedding", 1), (null, 1)], byType.Groups.Select(g => (g.Key, g.Count)));
        Assert.Equal(3, byType.AllGroups);
    }

    [Fact]
    public async Task Groups_by_type_only_the_occasions_the_filters_match()
    {
        await using var db = await CatalogWithOccasionsAsync();

        var count = await new SearchService(db, s_clock).CountAsync(new SearchQuery
        {
            Filters = new SearchFilters { EventTypes = ["wedding"] },
            Aggregate = new SearchAggregate { Count = SearchCount.Events, GroupBy = SearchGroupBy.EventType },
        });

        // The trip is an occasion too, but no asset of it matches a filter for weddings.
        Assert.Equal(1, count!.Total);
        Assert.Equal([("wedding", 1)], count.Groups.Select(g => (g.Key, g.Count)));
        Assert.Equal(1, count.AllGroups);
    }

    [Fact]
    public async Task Four_wedding_occasions_in_2026_count_as_four_whatever_the_number_of_photos()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        for (var n = 1; n <= 5; n++)
        {
            db.Assets.Add(CatalogData.Photo(Ids.Of(n), new DateTime(2026, n, 1)));
            db.Assets.Add(CatalogData.Photo(Ids.Of(n + 10), new DateTime(2026, n, 1, 20, 0, 0)));
        }

        db.Assets.Add(CatalogData.Photo(Ids.Of(30), new DateTime(2025, 6, 1)));
        await db.SaveChangesAsync();
        // Occasions 1 to 4 are weddings in 2026 with two photos each, 5 a party, and a wedding in 2025.
        for (var n = 1; n <= 5; n++)
        {
            db.Occasions.Add(new Occasion { StartsAt = new DateTime(2026, n, 1), EndsAt = new DateTime(2026, n, 1, 20, 0, 0), AssetCount = 2, Type = n <= 4 ? "wedding" : "party" });
        }

        db.Occasions.Add(new Occasion { StartsAt = new DateTime(2025, 6, 1), EndsAt = new DateTime(2025, 6, 1), AssetCount = 1, Type = "wedding" });
        await db.SaveChangesAsync();
        var ids = await db.Occasions.OrderBy(o => o.StartsAt).Select(o => o.Id).ToListAsync();
        for (var n = 1; n <= 5; n++)
        {
            db.OccasionAssets.AddRange(
                new OccasionAsset { EventId = ids[n], AssetId = Ids.Of(n) },
                new OccasionAsset { EventId = ids[n], AssetId = Ids.Of(n + 10) });
        }

        db.OccasionAssets.Add(new OccasionAsset { EventId = ids[0], AssetId = Ids.Of(30) });
        await db.SaveChangesAsync();

        var count = await new SearchService(db, s_clock).CountAsync(new SearchQuery
        {
            Filters = new SearchFilters { EventTypes = ["wedding"], Years = [2026] },
            Aggregate = new SearchAggregate { Count = SearchCount.Events },
        });

        Assert.Equal(4, count!.Total);
    }

    [Fact]
    public async Task Lists_only_the_occasions_of_a_type_and_offers_the_types_the_catalog_has()
    {
        await using var db = await CatalogWithOccasionsAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)));

        var trips = await queries.GetOccasionsPageAsync(1, "trip");
        var all = await queries.GetOccasionsPageAsync(1, "not a type");

        Assert.Equal([2L], trips.Items.Select(o => o.Id));
        Assert.Equal("trip", trips.Items[0].Type);
        Assert.Equal(2, all.Items.Count);
        // Trip and wedding, in the order of the types; the occasion without a type adds none.
        Assert.Equal(["wedding", "trip"], (await queries.GetFilterOptionsAsync()).EventTypes);
        Assert.Equal("wedding", (await queries.GetOccasionAsync(1))!.Type);
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
            new Occasion { Id = 1, StartsAt = new DateTime(2024, 12, 14, 9, 0, 0), EndsAt = new DateTime(2024, 12, 14, 10, 0, 0), PlaceLabel = "Lisbon", AssetCount = 2, Type = "wedding" },
            new Occasion { Id = 2, StartsAt = new DateTime(2025, 3, 2, 12, 0, 0), EndsAt = new DateTime(2025, 3, 2, 12, 0, 0), AssetCount = 1, Type = "trip" });
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
