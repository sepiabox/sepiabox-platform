using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;

namespace Sepiabox.Web.Tests;

/// <summary>Counts and groupings of the search DSL (S-026) on a real PostgreSQL catalog.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class SearchCountTests(CatalogFixture catalog)
{
    private static readonly FixedTime s_clock = new(new DateTimeOffset(2026, 10, 19, 9, 30, 0, TimeSpan.Zero));
    private static readonly SearchAggregate s_total = new();

    [Fact]
    public async Task Counts_4_of_the_10_assets_of_2019_when_counting_videos()
    {
        var assets = new List<Asset>();
        assets.AddRange(Enumerable.Range(1, 4).Select(n => Asset(n, new DateTime(2019, n, 1), kind: "video")));
        assets.AddRange(Enumerable.Range(5, 6).Select(n => Asset(n, new DateTime(2019, n, 1))));
        assets.Add(Asset(11, new DateTime(2020, 1, 1), kind: "video"));
        await using var db = await CatalogWithAsync([.. assets]);

        var count = await CountAsync(db, new SearchFilters { Kinds = ["video"], Years = [2019] }, s_total);

        Assert.Equal(4, count!.Total);
        Assert.Empty(count.Groups);
    }

    [Fact]
    public async Task Returns_the_3_years_with_the_most_lisbon_photos_largest_first()
    {
        var n = 0;
        Asset Lisbon(int year, string kind = "photo") => Asset(++n, new DateTime(year, 6, 1), city: "Lisbon", kind: kind);
        await using var db = await CatalogWithAsync(
            Lisbon(2015), Lisbon(2016), Lisbon(2016), Lisbon(2017), Lisbon(2017), Lisbon(2017), Lisbon(2017),
            Lisbon(2018), Lisbon(2018), Lisbon(2018), Lisbon(2019), Lisbon(2019),
            // Not Lisbon, or not photos: never counted.
            Asset(++n, new DateTime(2015, 1, 1), city: "Porto"), Asset(++n, new DateTime(2015, 1, 1), city: "Porto"),
            Asset(++n, new DateTime(2015, 1, 1), city: "Porto"), Lisbon(2015, "video"), Lisbon(2015, "video"), Lisbon(2015, "video"));

        var count = await CountAsync(
            db,
            new SearchFilters { Kinds = ["photo"], Places = new PlaceFilter { Cities = ["Lisbon"] } },
            new SearchAggregate { GroupBy = SearchGroupBy.Year, Top = 3 });

        Assert.Equal(12, count!.Total);
        Assert.Equal([new CountGroup("2017", 4), new CountGroup("2018", 3), new CountGroup("2016", 2)], count.Groups);
    }

    [Fact]
    public async Task Says_how_many_undated_assets_a_time_filter_leaves_out()
    {
        await using var db = await CatalogWithAsync(
            Asset(1, new DateTime(2019, 1, 1)), Asset(2, new DateTime(2019, 2, 1)), Asset(3, new DateTime(2020, 1, 1)),
            Asset(4, null), Asset(5, null), Asset(6, null), Asset(7, null, kind: "video"));

        var photos2019 = await CountAsync(db, new SearchFilters { Kinds = ["photo"], Years = [2019] }, s_total);
        var all = await CountAsync(db, new SearchFilters { Kinds = ["photo"] }, s_total);

        Assert.Equal((2, 3), (photos2019!.Total, photos2019.Undated!.Value));
        // Without a time filter the undated ones are in the total, and there is nothing to say.
        Assert.Equal(6, all!.Total);
        Assert.Null(all.Undated);
    }

    [Fact]
    public async Task Gives_no_number_for_a_count_with_a_description()
    {
        await using var db = await CatalogWithAsync(Asset(1, new DateTime(2019, 1, 1)));

        var count = await CountAsync(db, new SearchFilters { Visual = new VisualFilter { Text = "snow" } }, s_total);

        Assert.Null(count);
    }

    [Theory]
    [InlineData(SearchGroupBy.Month, "12=3,1=1,(none)=1")]
    [InlineData(SearchGroupBy.Kind, "photo=3,video=2")]
    [InlineData(SearchGroupBy.City, "Lisbon=2,Porto=2,(none)=1")]
    [InlineData(SearchGroupBy.Year, "2019=2,2018=1,2020=1,(none)=1")]
    public async Task Groups_by_month_kind_city_and_year_with_none_last_among_equals(string groupBy, string expected)
    {
        await using var db = await CatalogWithAsync(
            Asset(1, new DateTime(2019, 12, 24), city: "Lisbon"),
            Asset(2, new DateTime(2019, 12, 25), city: "Lisbon", kind: "video"),
            Asset(3, new DateTime(2020, 12, 1), city: "Porto"),
            Asset(4, new DateTime(2018, 1, 1), city: "Porto", kind: "video"),
            Asset(5, null));

        var count = await CountAsync(db, new SearchFilters(), new SearchAggregate { GroupBy = groupBy });

        Assert.Equal(5, count!.Total);
        Assert.Equal(expected, string.Join(',', count.Groups.Select(g => $"{g.Key ?? "(none)"}={g.Count}")));
    }

    [Fact]
    public async Task Counts_an_asset_once_per_source_name()
    {
        var both = Asset(1, new DateTime(2019, 1, 1), source: "HD da Ana");
        both.Sources.Add(new AssetSource { AssetId = Ids.Of(1), Type = "local", Name = "Celular do Bruno", Path = "b/1.jpg" });
        // Two paths on the same drive: still one asset of that source.
        both.Sources.Add(new AssetSource { AssetId = Ids.Of(1), Type = "local", Name = "HD da Ana", Path = "copia/1.jpg" });
        await using var db = await CatalogWithAsync(both, Asset(2, new DateTime(2019, 1, 1), source: "HD da Ana"));

        var count = await CountAsync(db, new SearchFilters(), new SearchAggregate { GroupBy = SearchGroupBy.Source });

        Assert.Equal(2, count!.Total);
        Assert.Equal([new CountGroup("HD da Ana", 2), new CountGroup("Celular do Bruno", 1)], count.Groups);
    }

    private static async Task<CountResult?> CountAsync(CatalogDbContext db, SearchFilters filters, SearchAggregate aggregate) =>
        await new SearchService(db, s_clock).CountAsync(new SearchQuery { Filters = filters, Aggregate = aggregate });

    private async Task<CatalogDbContext> CatalogWithAsync(params Asset[] assets)
    {
        var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.AddRange(assets);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static Asset Asset(int n, DateTime? capturedAt, string? city = null, string kind = "photo", string source = "HD") => new()
    {
        Id = Ids.Of(n),
        Kind = kind,
        ContentType = kind == "video" ? "video/mp4" : "image/jpeg",
        SizeBytes = 1000,
        CapturedAt = capturedAt,
        PlaceCity = city,
        PlaceCountry = city is null ? null : "Portugal",
        ImportedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
        Sources = [new AssetSource { AssetId = Ids.Of(n), Type = "local", Name = source, Path = $"{n}.jpg" }],
    };
}
