using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Events;

namespace Sepiabox.Worker.Tests.Events;

/// <summary><c>events rebuild</c> on a real PostgreSQL catalog (S-023): stored occasions, idempotence and stable ids.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class OccasionRebuildTests(CatalogFixture fixture)
{
    private static readonly DateTime s_start = new(2024, 12, 14, 10, 0, 0);

    [Fact]
    public async Task Photos_7_hours_apart_end_up_in_different_occasions()
    {
        await using var db = await CatalogWithAsync(Asset(1, 0), Asset(2, 7));

        var result = await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        Assert.Equal(new OccasionRebuildResult(2, 2, 2, 0, 0), result);
        Assert.Equal([[Id(1)], [Id(2)]], await AssetsPerOccasionAsync(db));
    }

    [Fact]
    public async Task Photos_1_hour_and_60_km_apart_end_up_in_different_occasions()
    {
        await using var db = await CatalogWithAsync(Asset(1, 0, 38.72), Asset(2, 1, 38.72 + 60 / 111.195));

        await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        Assert.Equal([[Id(1)], [Id(2)]], await AssetsPerOccasionAsync(db));
    }

    [Fact]
    public async Task Running_it_twice_gives_identical_results_and_changes_nothing_the_second_time()
    {
        await using var db = await CatalogWithAsync(
            Asset(1, 0, 38.72, city: "Lisbon"), Asset(2, 1, 38.73, city: "Lisbon"), Asset(3, 20, 41.15, city: "Porto"), Asset(4, null), Asset(5, 50));

        var first = await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        var afterFirst = await DumpAsync(db);
        var second = await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        var afterSecond = await DumpAsync(db);

        Assert.Equal(new OccasionRebuildResult(3, 4, 3, 0, 0), first);
        Assert.Equal(new OccasionRebuildResult(3, 4, 0, 0, 0), second);
        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(3, afterSecond.Count);
    }

    [Fact]
    public async Task Undated_assets_belong_to_no_occasion()
    {
        await using var db = await CatalogWithAsync(Asset(1, 0), Asset(2, null), Asset(3, null));

        await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        Assert.Equal([Id(1)], await db.OccasionAssets.Select(l => l.AssetId).ToListAsync());
        Assert.Equal(1, await db.Occasions.CountAsync());
    }

    [Fact]
    public async Task Stores_the_dates_the_count_the_place_and_the_centre_of_an_occasion()
    {
        await using var db = await CatalogWithAsync(
            Asset(1, 0, 38.0, city: "Lisbon"), Asset(2, 30, 38.0, city: "Lisbon"), Asset(3, 3, 40.0, city: "Lisbon"));

        await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        var occasions = await db.Occasions.AsNoTracking().OrderBy(o => o.StartsAt).ToListAsync();
        // 38.0 and 40.0 are 222 km apart: three assets, a split after the second (hour 3 to hour 30 is a gap, too).
        Assert.Equal(3, occasions.Count);
        var first = occasions[0];
        Assert.Equal((s_start, s_start, 1, "Lisbon", null), (first.StartsAt, first.EndsAt, first.AssetCount, first.PlaceLabel, first.Type));
        Assert.Equal(4326, first.Centroid!.SRID);
        Assert.Equal(38.0, first.Centroid.Y, 4);
    }

    [Fact]
    public async Task An_occasion_keeps_its_id_and_its_type_when_nothing_in_it_changes()
    {
        await using var db = await CatalogWithAsync(Asset(1, 0), Asset(2, 1), Asset(3, 20));
        await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        var first = await db.Occasions.OrderBy(o => o.StartsAt).FirstAsync();
        first.Type = "wedding";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var again = await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        Assert.Equal(0, again.Updated + again.Created + again.Removed);
        var kept = await db.Occasions.AsNoTracking().SingleAsync(o => o.Id == first.Id);
        Assert.Equal("wedding", kept.Type);
    }

    [Fact]
    public async Task An_occasion_that_gains_an_asset_keeps_its_id_and_loses_its_type()
    {
        await using var db = await CatalogWithAsync(Asset(1, 0), Asset(2, 1), Asset(3, 20));
        await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        var first = await db.Occasions.OrderBy(o => o.StartsAt).FirstAsync();
        first.Type = "wedding";
        await db.SaveChangesAsync();
        db.Assets.Add(Asset(4, 3));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        Assert.Equal((0, 1, 0), (result.Created, result.Updated, result.Removed));
        var grown = await db.Occasions.AsNoTracking().SingleAsync(o => o.Id == first.Id);
        Assert.Equal((3, null), (grown.AssetCount, grown.Type));
        Assert.Equal([Id(1), Id(2), Id(4)], await db.OccasionAssets.Where(l => l.EventId == first.Id).Select(l => l.AssetId).OrderBy(i => i).ToListAsync());
    }

    [Fact]
    public async Task An_occasion_that_gains_an_earlier_asset_keeps_its_id_and_loses_its_type()
    {
        await using var db = await CatalogWithAsync(Asset(1, 1), Asset(2, 2), Asset(3, 20));
        await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        var first = await db.Occasions.OrderBy(o => o.StartsAt).FirstAsync();
        first.Type = "wedding";
        await db.SaveChangesAsync();
        // Taken 4 hours before the first photo: the occasion now starts with a different asset.
        db.Assets.Add(Asset(4, -3));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        Assert.Equal((0, 1, 0), (result.Created, result.Updated, result.Removed));
        var grown = await db.Occasions.AsNoTracking().SingleAsync(o => o.Id == first.Id);
        Assert.Equal((3, null), (grown.AssetCount, grown.Type));
        Assert.Equal(3, await db.OccasionAssets.CountAsync(l => l.EventId == first.Id));
    }

    [Fact]
    public async Task An_occasion_where_one_asset_leaves_and_another_joins_loses_its_type_though_the_count_is_the_same()
    {
        await using var db = await CatalogWithAsync(Asset(1, 0), Asset(2, 1));
        await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        var occasion = await db.Occasions.SingleAsync();
        occasion.Type = "wedding";
        await db.SaveChangesAsync();
        db.Assets.Remove(await db.Assets.SingleAsync(a => a.Id == Id(2)));
        db.Assets.Add(Asset(3, 2));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        var after = await db.Occasions.AsNoTracking().SingleAsync();
        Assert.Equal((occasion.Id, 2, null), (after.Id, after.AssetCount, after.Type));
        Assert.Equal([Id(1), Id(3)], await db.OccasionAssets.Select(l => l.AssetId).OrderBy(i => i).ToListAsync());
    }

    [Fact]
    public async Task Occasions_that_merge_keep_the_id_of_the_one_that_gives_most_of_the_assets()
    {
        // One photo alone, and three together 8 hours later; a photo between them joins all into one.
        await using var db = await CatalogWithAsync(Asset(1, 0), Asset(2, 8), Asset(3, 9), Asset(4, 10));
        await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        var ids = await db.Occasions.OrderBy(o => o.StartsAt).Select(o => o.Id).ToListAsync();
        db.Assets.Add(Asset(5, 4));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        Assert.Equal((0, 1, 1), (result.Created, result.Updated, result.Removed));
        Assert.Equal([ids[1]], await db.Occasions.Select(o => o.Id).ToListAsync());
        Assert.Equal(5, await db.OccasionAssets.CountAsync(l => l.EventId == ids[1]));
    }

    [Fact]
    public async Task The_larger_part_of_a_split_keeps_the_id_and_the_type_goes_with_the_new_one()
    {
        // Ten photos in an hour; the first is then moved 10 hours earlier, so it becomes an occasion of its own.
        await using var db = await CatalogWithAsync(Enumerable.Range(1, 10).Select(n => Asset(n, n % 6)).ToArray());
        await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        var original = await db.Occasions.SingleAsync();
        original.Type = "wedding";
        await db.SaveChangesAsync();
        (await db.Assets.SingleAsync(a => a.Id == Id(1))).CapturedAt = s_start.AddHours(-10);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        Assert.Equal((1, 1, 0), (result.Created, result.Updated, result.Removed));
        var kept = await db.Occasions.AsNoTracking().SingleAsync(o => o.Id == original.Id);
        Assert.Equal(9, kept.AssetCount);
        Assert.Equal(9, await db.OccasionAssets.CountAsync(l => l.EventId == original.Id));
        // One photo left it, so the nine are not the same occasion as before: no type until it is classified again.
        Assert.Null(kept.Type);
        var alone = await db.Occasions.AsNoTracking().SingleAsync(o => o.Id != original.Id);
        Assert.Equal(new[] { Id(1) }, await db.OccasionAssets.Where(l => l.EventId == alone.Id).Select(l => l.AssetId).ToArrayAsync());
    }

    [Fact]
    public async Task Equal_halves_of_a_split_give_the_lower_id_to_the_earlier_part()
    {
        await using var db = await CatalogWithAsync(Asset(1, 0), Asset(2, 1), Asset(3, 2), Asset(4, 3));
        await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        var id = await db.Occasions.Select(o => o.Id).SingleAsync();
        // A gap of 10 hours between the second and the third photo.
        foreach (var n in new[] { 3, 4 })
        {
            (await db.Assets.SingleAsync(a => a.Id == Id(n))).CapturedAt = s_start.AddHours(10 + n);
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        var parts = await db.Occasions.AsNoTracking().OrderBy(o => o.StartsAt).ToListAsync();
        Assert.Equal(id, parts[0].Id);
        Assert.True(parts[1].Id > id);
        Assert.Equal([[Id(1), Id(2)], [Id(3), Id(4)]], await AssetsPerOccasionAsync(db));
    }

    [Fact]
    public async Task A_part_whose_only_candidate_id_went_to_a_larger_part_gets_a_new_id()
    {
        // Occasion A holds photos 1 to 3 (hours 0 to 2) and occasion B photos 4 and 5 (hours 20 and 21).
        await using var db = await CatalogWithAsync(Asset(1, 0), Asset(2, 1), Asset(3, 2), Asset(4, 20), Asset(5, 21));
        await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        var ids = await db.Occasions.OrderBy(o => o.StartsAt).Select(o => o.Id).ToListAsync();
        // Photo 3 moves to hour 12, 11 hours after photo 2 and 8 before photo 4, so it stands alone. It shares only A
        // with the old occasions, and A's id goes to photos 1 and 2, which share more of it.
        (await db.Assets.SingleAsync(a => a.Id == Id(3))).CapturedAt = s_start.AddHours(12);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        var after = await db.Occasions.AsNoTracking().OrderBy(o => o.StartsAt).ToListAsync();
        Assert.Equal(3, after.Count);
        Assert.Equal(ids[0], after[0].Id);
        Assert.Equal(ids[1], after[2].Id);
        Assert.DoesNotContain(after[1].Id, ids);
    }

    [Fact]
    public async Task Two_occasions_that_merge_keep_the_first_ones_id_and_the_other_goes()
    {
        await using var db = await CatalogWithAsync(Asset(1, 0), Asset(2, 8));
        await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        var ids = await db.Occasions.OrderBy(o => o.StartsAt).Select(o => o.Id).ToListAsync();
        // A photo in between joins them (3 hours after the first, 5 before the second).
        db.Assets.Add(Asset(3, 4));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        Assert.Equal((0, 1, 1), (result.Created, result.Updated, result.Removed));
        Assert.Equal([ids[0]], await db.Occasions.Select(o => o.Id).ToListAsync());
        Assert.Equal(3, await db.OccasionAssets.CountAsync(l => l.EventId == ids[0]));
    }

    [Fact]
    public async Task An_occasion_that_splits_keeps_its_id_on_the_part_with_its_first_asset()
    {
        await using var db = await CatalogWithAsync(Asset(1, 0), Asset(2, 4), Asset(3, 8));
        await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        var id = await db.Occasions.Select(o => o.Id).SingleAsync();
        // Removing the middle photo opens a gap of 8 hours.
        db.Assets.Remove(await db.Assets.SingleAsync(a => a.Id == Id(2)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        Assert.Equal((1, 1, 0), (result.Created, result.Updated, result.Removed));
        var occasions = await db.Occasions.AsNoTracking().OrderBy(o => o.StartsAt).ToListAsync();
        Assert.Equal(id, occasions[0].Id);
        Assert.Equal([[Id(1)], [Id(3)]], await AssetsPerOccasionAsync(db));
    }

    [Fact]
    public async Task An_occasion_whose_assets_are_all_gone_is_removed()
    {
        await using var db = await CatalogWithAsync(Asset(1, 0), Asset(2, 20));
        await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        db.Assets.Remove(await db.Assets.SingleAsync(a => a.Id == Id(2)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Removed);
        Assert.Equal([[Id(1)]], await AssetsPerOccasionAsync(db));
    }

    [Fact]
    public async Task An_asset_whose_date_is_removed_leaves_its_occasion()
    {
        await using var db = await CatalogWithAsync(Asset(1, 0), Asset(2, 1));
        await new OccasionRebuild(db).RunAsync(CancellationToken.None);
        (await db.Assets.SingleAsync(a => a.Id == Id(2))).CapturedAt = null;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new OccasionRebuild(db).RunAsync(CancellationToken.None);

        Assert.Equal([[Id(1)]], await AssetsPerOccasionAsync(db));
        Assert.Equal(1, (await db.Occasions.SingleAsync()).AssetCount);
    }

    [Fact]
    public async Task Rebuilding_an_empty_catalog_does_nothing()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();

        Assert.Equal(new OccasionRebuildResult(0, 0, 0, 0, 0), await new OccasionRebuild(db).RunAsync(CancellationToken.None));
    }

    private async Task<CatalogDbContext> CatalogWithAsync(params Asset[] assets)
    {
        var db = await fixture.CreateMigratedDatabaseAsync();
        db.Assets.AddRange(assets);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    // Each occasion's asset ids, in the order of their start.
    private static async Task<List<string[]>> AssetsPerOccasionAsync(CatalogDbContext db)
    {
        var occasions = await db.Occasions.AsNoTracking().OrderBy(o => o.StartsAt).ThenBy(o => o.Id).Select(o => o.Id).ToListAsync();
        var links = await db.OccasionAssets.AsNoTracking().ToListAsync();
        return [.. occasions.Select(id => links.Where(l => l.EventId == id).Select(l => l.AssetId).Order(StringComparer.Ordinal).ToArray())];
    }

    // Everything a rebuild writes, as comparable text.
    private static async Task<List<string>> DumpAsync(CatalogDbContext db)
    {
        var links = await db.OccasionAssets.AsNoTracking().ToListAsync();
        return [.. (await db.Occasions.AsNoTracking().OrderBy(o => o.Id).ToListAsync()).Select(o =>
            $"{o.Id}|{o.StartsAt:O}|{o.EndsAt:O}|{o.Centroid?.X}|{o.Centroid?.Y}|{o.PlaceLabel}|{o.AssetCount}|{o.Type}|"
            + string.Join(",", links.Where(l => l.EventId == o.Id).Select(l => l.AssetId).Order(StringComparer.Ordinal)))];
    }

    private static string Id(int n) => n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);

    private static Asset Asset(int n, int? hours, double? latitude = null, string? city = null) => new()
    {
        Id = Id(n),
        Kind = "photo",
        ContentType = "image/jpeg",
        SizeBytes = 1000,
        CapturedAt = hours is { } h ? s_start.AddHours(h) : null,
        Location = latitude is { } lat ? new Point(-9.14, lat) { SRID = 4326 } : null,
        PlaceCity = city,
        PlaceCountry = city is null ? null : "Portugal",
        ImportedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };
}
