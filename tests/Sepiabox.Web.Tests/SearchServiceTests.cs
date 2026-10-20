using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>Each filter of the search DSL v1 and their combinations on a real PostgreSQL catalog (S-013).</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class SearchServiceTests(CatalogFixture catalog)
{
    // 19 October 2026, mid-morning UTC: "five years ago today" is 2021-10-19.
    private static readonly FixedTime s_clock = new(new DateTimeOffset(2026, 10, 19, 9, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task Finds_only_the_lisbon_assets_from_december_when_filtering_by_city_and_month()
    {
        await using var db = await CatalogWithAsync(
            Asset(1, new DateTime(2019, 12, 24, 20, 0, 0), city: "Lisbon"),
            Asset(2, new DateTime(2020, 12, 1, 9, 0, 0), city: "Lisbon"),
            Asset(3, new DateTime(2019, 11, 30, 23, 59, 59), city: "Lisbon"),
            Asset(4, new DateTime(2019, 12, 24, 20, 0, 0), city: "Porto"),
            Asset(5, new DateTime(2019, 6, 10, 12, 0, 0), city: "Porto"),
            Asset(6, new DateTime(2019, 12, 2, 8, 0, 0)));

        var found = await IdsAsync(db, new SearchFilters { Places = new PlaceFilter { Cities = ["Lisbon"] }, Months = [12] });

        Assert.Equal([2, 1], found);
    }

    [Fact]
    public async Task Finds_only_the_assets_from_five_years_ago_today_with_the_clock_at_2026_10_19()
    {
        await using var db = await CatalogWithAsync(
            Asset(1, new DateTime(2021, 10, 19, 0, 0, 0)),
            Asset(2, new DateTime(2021, 10, 19, 23, 59, 59)),
            Asset(3, new DateTime(2021, 10, 18, 23, 59, 59)),
            Asset(4, new DateTime(2021, 10, 20, 0, 0, 0)),
            Asset(5, new DateTime(2020, 10, 19, 12, 0, 0)),
            Asset(6, null));

        var found = await IdsAsync(db, new SearchFilters { OnThisDay = new OnThisDayFilter { YearsAgo = 5 } });

        Assert.Equal([2, 1], found);
    }

    [Fact]
    public async Task Finds_this_day_in_every_earlier_year_but_not_this_year_when_years_ago_is_absent()
    {
        await using var db = await CatalogWithAsync(
            Asset(1, new DateTime(2021, 10, 19, 8, 0, 0)),
            Asset(2, new DateTime(1998, 10, 19, 8, 0, 0)),
            Asset(3, new DateTime(2026, 10, 19, 8, 0, 0)),
            Asset(4, new DateTime(2025, 10, 18, 8, 0, 0)));

        var found = await IdsAsync(db, new SearchFilters { OnThisDay = new OnThisDayFilter() });

        Assert.Equal([1, 2], found);
    }

    [Fact]
    public async Task Takes_today_in_the_configured_time_zone_when_one_is_set()
    {
        // 23:30 UTC on 18 October is already 19 October in Lisbon (UTC+1 in summer time).
        var late = new FixedTime(new DateTimeOffset(2026, 10, 18, 23, 30, 0, TimeSpan.Zero));
        var lisbon = new SearchOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon") };
        await using var db = await CatalogWithAsync(Asset(1, new DateTime(2021, 10, 19, 8, 0, 0)), Asset(2, new DateTime(2021, 10, 18, 8, 0, 0)));
        var query = new SearchQuery { Filters = new SearchFilters { OnThisDay = new OnThisDayFilter { YearsAgo = 5 } } };

        var inLisbon = await new SearchService(db, late, lisbon).ExecuteAsync(query);
        var inUtc = await new SearchService(db, late).ExecuteAsync(query);

        Assert.Equal(Ids.Of(1), Assert.Single(inLisbon.Hits).Id);
        Assert.Equal(Ids.Of(2), Assert.Single(inUtc.Hits).Id);
    }

    [Fact]
    public async Task Combines_several_filters_with_and()
    {
        await using var db = await CatalogWithAsync(
            Asset(1, new DateTime(2019, 7, 14, 10, 0, 0), city: "Lisbon", source: "HD da Ana"),
            Asset(2, new DateTime(2019, 7, 14, 10, 0, 0), city: "Lisbon", source: "HD da Ana", kind: "video"),
            Asset(3, new DateTime(2019, 7, 14, 10, 0, 0), city: "Porto", source: "HD da Ana"),
            Asset(4, new DateTime(2018, 7, 14, 10, 0, 0), city: "Lisbon", source: "HD da Ana"),
            Asset(5, new DateTime(2019, 7, 14, 10, 0, 0), city: "Lisbon", source: "HD do Jonathas"));

        var found = await IdsAsync(db, new SearchFilters
        {
            Kinds = ["photo"],
            Years = [2019],
            Places = new PlaceFilter { Cities = ["Lisbon"] },
            Sources = ["HD da Ana"],
        });

        Assert.Equal([1], found);
    }

    [Fact]
    public async Task Matches_any_value_of_one_list()
    {
        await using var db = await CatalogWithAsync(
            Asset(1, new DateTime(2018, 1, 1)), Asset(2, new DateTime(2019, 1, 1)), Asset(3, new DateTime(2020, 1, 1)));

        Assert.Equal([3, 1], await IdsAsync(db, new SearchFilters { Years = [2018, 2020] }));
    }

    [Fact]
    public async Task Filters_by_kind_date_range_and_undated()
    {
        await using var db = await CatalogWithAsync(
            Asset(1, new DateTime(2019, 1, 1, 0, 0, 0)),
            Asset(2, new DateTime(2019, 12, 31, 23, 59, 59), kind: "video"),
            Asset(3, new DateTime(2018, 12, 31, 23, 59, 59)),
            Asset(4, new DateTime(2020, 1, 1, 0, 0, 0)),
            Asset(5, null),
            Asset(6, null, kind: "video"));

        Assert.Equal([2, 1], await IdsAsync(db, new SearchFilters { DateFrom = new DateOnly(2019, 1, 1), DateTo = new DateOnly(2019, 12, 31) }));
        Assert.Equal([4, 2, 1], await IdsAsync(db, new SearchFilters { DateFrom = new DateOnly(2019, 1, 1) }));
        Assert.Equal([2, 6], await IdsAsync(db, new SearchFilters { Kinds = ["video"] }));
        Assert.Equal([5, 6], await IdsAsync(db, new SearchFilters { UndatedOnly = true }));
    }

    [Fact]
    public async Task Matches_city_names_and_sources_ignoring_case_and_accents()
    {
        await using var db = await CatalogWithAsync(
            Asset(1, new DateTime(2019, 1, 1), city: "Lisbon", source: "HD da Ana"),
            Asset(2, new DateTime(2019, 1, 2), city: "São Paulo", country: "Brazil"),
            Asset(3, new DateTime(2019, 1, 3), city: "Porto"));
        db.GeoNames.Add(City(3448439, "São Paulo", "Sao Paulo", "BR", "Brazil", -23.5475, -46.63611));
        await db.SaveChangesAsync();

        Assert.Equal([1], await IdsAsync(db, new SearchFilters { Places = new PlaceFilter { Cities = ["LISBON"] } }));
        Assert.Equal([2], await IdsAsync(db, new SearchFilters { Places = new PlaceFilter { Cities = ["sao paulo"] } }));
        Assert.Equal([2], await IdsAsync(db, new SearchFilters { Places = new PlaceFilter { Cities = ["SÃO PAULO"] } }));
        Assert.Equal([1], await IdsAsync(db, new SearchFilters { Sources = ["hd DA ana"] }));
    }

    [Fact]
    public async Task Filters_by_country_code_through_geonames_country_names()
    {
        await using var db = await CatalogWithAsync(
            Asset(1, new DateTime(2019, 1, 1), city: "Lisbon", country: "Portugal"),
            Asset(2, new DateTime(2019, 1, 2), city: "Madrid", country: "Spain"),
            Asset(3, new DateTime(2019, 1, 3)));
        db.GeoNames.AddRange(
            City(2267057, "Lisbon", "Lisbon", "PT", "Portugal", 38.71667, -9.13333),
            City(3117735, "Madrid", "Madrid", "ES", "Spain", 40.4165, -3.70256));
        await db.SaveChangesAsync();

        Assert.Equal([1], await IdsAsync(db, new SearchFilters { Places = new PlaceFilter { Countries = ["PT"] } }));
        Assert.Equal([2, 1], await IdsAsync(db, new SearchFilters { Places = new PlaceFilter { Countries = ["PT", "ES"] } }));
        Assert.Empty(await IdsAsync(db, new SearchFilters { Places = new PlaceFilter { Countries = ["FR"] } }));
    }

    [Fact]
    public async Task Sorts_both_ways_with_undated_last_and_stops_at_the_limit()
    {
        await using var db = await CatalogWithAsync(
            Asset(1, new DateTime(2019, 1, 1)), Asset(2, null), Asset(3, new DateTime(2020, 1, 1)), Asset(4, new DateTime(2018, 1, 1)));
        var service = new SearchService(db, s_clock);

        var newest = await service.ExecuteAsync(new SearchQuery());
        var oldest = await service.ExecuteAsync(new SearchQuery { Sort = SearchSort.CapturedAtAsc });
        var two = await service.ExecuteAsync(new SearchQuery { Limit = 2 });

        Assert.Equal([Ids.Of(3), Ids.Of(1), Ids.Of(4), Ids.Of(2)], newest.Hits.Select(h => h.Id));
        Assert.Equal([Ids.Of(4), Ids.Of(1), Ids.Of(3), Ids.Of(2)], oldest.Hits.Select(h => h.Id));
        Assert.Equal([Ids.Of(3), Ids.Of(1)], two.Hits.Select(h => h.Id));
        Assert.True(two.HasMore);
        Assert.False(newest.HasMore);
    }

    [Fact]
    public async Task Treats_quotes_and_sql_in_a_value_as_text()
    {
        await using var db = await CatalogWithAsync(Asset(1, new DateTime(2019, 1, 1), city: "Lisbon", source: "HD"));

        var found = await IdsAsync(db, new SearchFilters
        {
            Places = new PlaceFilter { Cities = ["Lisbon'); DROP TABLE assets; --"] },
            Sources = ["' OR '1'='1"],
        });

        Assert.Empty(found);
        Assert.Equal(1, await db.Assets.CountAsync());
    }

    [Fact]
    public async Task The_web_lists_results_with_thumbnails_and_offers_the_catalog_s_years_cities_and_sources()
    {
        await using var db = await CatalogWithAsync(
            Asset(1, new DateTime(2019, 12, 24), city: "Lisbon", source: "HD da Ana"),
            Asset(2, new DateTime(2021, 3, 1), city: "Porto", source: "HD do Jonathas"),
            Asset(3, null));
        db.Derivatives.Add(new Derivative
        {
            AssetId = Ids.Of(1),
            Name = DerivativeNames.Thumbnail,
            BlobPath = $"{Ids.Of(1)}/thumb-256.webp",
            Width = 256,
            Height = 192,
            ContentType = "image/webp",
        });
        await db.SaveChangesAsync();
        var queries = new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)), s_clock);

        var page = await queries.SearchAsync(new SearchQuery { Filters = new SearchFilters { Places = new PlaceFilter { Cities = ["Lisbon"] } } });
        var options = await queries.GetFilterOptionsAsync();

        var item = Assert.Single(page.Items);
        Assert.Equal((Ids.Of(1), true), (item.Id, item.HasThumbnail));
        Assert.Equal([2021, 2019], options.Years);
        Assert.Equal(["Lisbon", "Porto"], options.Cities);
        Assert.Equal(["HD", "HD da Ana", "HD do Jonathas"], options.Sources);
    }

    [Fact]
    public async Task The_filter_bar_s_address_and_the_api_show_only_matching_assets_from_a_real_catalog()
    {
        string connection;
        await using (var db = await CatalogWithAsync(
            Asset(1, new DateTime(2019, 12, 24, 20, 0, 0), city: "Lisbon"),
            Asset(2, new DateTime(2019, 7, 1, 9, 0, 0), city: "Lisbon"),
            Asset(3, new DateTime(2019, 12, 24, 20, 0, 0), city: "Porto")))
        {
            connection = catalog.ConnectionStringFor(db);
        }

        await using var web = new WebFactory(connection);
        using var http = web.CreateSignedInClient();

        // The bar's form, followed to its ?q= address, as a browser would.
        var html = await http.GetStringAsync(new Uri("/?filter=1&year=&month=12&kind=&city=Lisbon&source=", UriKind.Relative));
        using var api = await http.PostAsync(
            new Uri("/api/search", UriKind.Relative),
            new StringContent("""{"filters": {"places": {"cities": ["Lisbon"]}, "months": [12]}}""", System.Text.Encoding.UTF8, "application/json"));
        var json = await api.Content.ReadAsStringAsync();

        Assert.Contains($"photos/{Ids.Of(1)}?q=", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"photos/{Ids.Of(2)}", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"photos/{Ids.Of(3)}", html, StringComparison.Ordinal);
        Assert.Contains("December 2019", html, StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.OK, api.StatusCode);
        Assert.Contains(Ids.Of(1), json, StringComparison.Ordinal);
        Assert.DoesNotContain(Ids.Of(3), json, StringComparison.Ordinal);
    }

    private async Task<CatalogDbContext> CatalogWithAsync(params Asset[] assets)
    {
        var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.AddRange(assets);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static async Task<List<int>> IdsAsync(CatalogDbContext db, SearchFilters filters)
    {
        var result = await new SearchService(db, s_clock).ExecuteAsync(new SearchQuery { Filters = filters });
        return [.. result.Hits.Select(h => int.Parse(h.Id[^8..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture))];
    }

    private static Asset Asset(int n, DateTime? capturedAt, string? city = null, string? country = null, string source = "HD", string kind = "photo") => new()
    {
        Id = Ids.Of(n),
        Kind = kind,
        ContentType = kind == "video" ? "video/mp4" : "image/jpeg",
        SizeBytes = 1000,
        CapturedAt = capturedAt,
        PlaceCity = city,
        PlaceCountry = country ?? (city is null ? null : "Portugal"),
        ImportedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
        Sources = [new AssetSource { AssetId = Ids.Of(n), Type = "local", Name = source, Path = $"{n}.jpg" }],
    };

    private static GeoName City(long id, string name, string ascii, string countryCode, string countryName, double latitude, double longitude) => new()
    {
        GeonameId = id,
        Name = name,
        AsciiName = ascii,
        CountryCode = countryCode,
        CountryName = countryName,
        Population = 1000,
        Location = new Point(longitude, latitude) { SRID = 4326 },
    };
}
