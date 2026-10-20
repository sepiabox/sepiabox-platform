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

    public static TheoryData<string, int?, int[]> LeapDays => new()
    {
        // 28 February of a common year: the 28ths of earlier years and the 29ths of leap years.
        { "2027-02-28", null, [1, 2, 4] },
        // 29 February: only the 29ths of earlier leap years.
        { "2028-02-29", null, [1] },
        // From 29 February, a common year N years ago gives its 28th...
        { "2028-02-29", 1, [3] },
        // ...and a leap year N years ago its 29th.
        { "2028-02-29", 4, [1] },
        // From 28 February of a common year, a leap year N years ago gives its 28th and its 29th.
        { "2027-02-28", 3, [1, 2] },
        // And a common year N years ago only its 28th.
        { "2027-02-28", 2, [4] },
    };

    [Theory]
    [MemberData(nameof(LeapDays))]
    public async Task Handles_29_february_on_this_day(string today, int? yearsAgo, int[] expected)
    {
        var clock = new FixedTime(new DateTimeOffset(DateTime.Parse(today, System.Globalization.CultureInfo.InvariantCulture).AddHours(12), TimeSpan.Zero));
        await using var db = await CatalogWithAsync(
            Asset(1, new DateTime(2024, 2, 29, 10, 0, 0)),
            Asset(2, new DateTime(2024, 2, 28, 10, 0, 0)),
            Asset(3, new DateTime(2027, 2, 28, 10, 0, 0)),
            Asset(4, new DateTime(2025, 2, 28, 10, 0, 0)),
            Asset(5, new DateTime(2024, 3, 1, 10, 0, 0)));
        var query = new SearchQuery { Filters = new SearchFilters { OnThisDay = new OnThisDayFilter { YearsAgo = yearsAgo } } };

        var found = await new SearchService(db, clock).ExecuteAsync(query);

        Assert.Equal(expected.Order(), found.Hits.Select(h => int.Parse(h.Id[^8..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)).Order());
    }

    [Fact]
    public async Task Answers_a_date_range_that_ends_on_the_last_day_there_is()
    {
        await using var db = await CatalogWithAsync(Asset(1, new DateTime(2019, 1, 1)), Asset(2, new DateTime(2020, 1, 1)), Asset(3, null));

        Assert.Equal([2], await IdsAsync(db, new SearchFilters { DateFrom = new DateOnly(2019, 6, 1), DateTo = DateOnly.MaxValue }));
        Assert.Equal([2, 1], await IdsAsync(db, new SearchFilters { DateFrom = DateOnly.MinValue, DateTo = DateOnly.MaxValue }));
    }

    [Fact]
    public async Task Matches_cities_through_the_lower_case_name_indexes()
    {
        await using var db = await CatalogWithAsync();
        var service = new SearchService(db, s_clock);
        var query = new SearchQuery { Filters = new SearchFilters { Places = new PlaceFilter { Cities = ["Lisbon"] } } };

        // The expressions EF writes are the ones the migration indexes...
        var sql = service.Filtered(query).ToQueryString();
        Assert.Contains("lower(g.ascii_name)", sql, StringComparison.Ordinal);
        Assert.Contains("lower(g.name)", sql, StringComparison.Ordinal);

        // ...and the planner can use those indexes for them (forced here: a test table is too small to prefer them).
        await using var connection = new Npgsql.NpgsqlConnection(catalog.ConnectionStringFor(db));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SET enable_seqscan = off;
            EXPLAIN SELECT g.name FROM geonames AS g
            WHERE lower(g.ascii_name) = ANY (ARRAY['lisbon']) OR lower(g.name) = ANY (ARRAY['lisbon']);
            """;
        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }
        }

        var text = string.Join('\n', plan);
        Assert.Contains("ix_geonames_lower_ascii_name", text, StringComparison.Ordinal);
        Assert.Contains("ix_geonames_lower_name", text, StringComparison.Ordinal);
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

        // The last day there is, through the address and the API: an answer, never an error page.
        var lastDay = new SearchQuery { Filters = new SearchFilters { DateTo = DateOnly.MaxValue } };
        using var page = await http.GetAsync(new Uri("/?q=" + SearchQueryJson.Encode(lastDay), UriKind.Relative));
        using var apiLastDay = await http.PostAsync(
            new Uri("/api/search", UriKind.Relative),
            new StringContent("""{"filters": {"dateTo": "9999-12-31"}}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(System.Net.HttpStatusCode.OK, page.StatusCode);
        Assert.Contains($"photos/{Ids.Of(1)}", await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.OK, apiLastDay.StatusCode);
    }

    [Fact]
    public async Task Puts_the_asset_closest_to_the_description_first()
    {
        await using var db = await CatalogWithEmbeddingsAsync();
        // Close to asset 2 (axis 1) and a little towards asset 3's second axis.
        var embedder = new FakeQueryEmbedder(embed: _ => FakeQueryEmbedder.Axis(1, 1, 2, 0.1f));

        var result = await new SearchService(db, s_clock, embedder: embedder).ExecuteAsync(Visual("snow"));

        // Similarities about 0.99 (asset 2), 0.95 (asset 5) and 0.86 (asset 3); asset 1 is orthogonal, below 0.2.
        Assert.Equal([Ids.Of(2), Ids.Of(5), Ids.Of(3)], result.Hits.Select(h => h.Id));
        Assert.Equal(["snow"], embedder.Texts);
        Assert.InRange(result.Hits[0].Score!.Value, 0.99, 1.0);
        Assert.True(result.Hits[0].Score > result.Hits[1].Score && result.Hits[1].Score > result.Hits[2].Score);
    }

    [Fact]
    public async Task Combines_a_description_with_years_and_keeps_the_similarity_order()
    {
        await using var db = await CatalogWithEmbeddingsAsync();
        var embedder = new FakeQueryEmbedder(embed: _ => FakeQueryEmbedder.Axis(1, 1, 2, 0.1f));
        var query = Visual("snow") with { Filters = new SearchFilters { Visual = new VisualFilter { Text = "snow" }, Years = [2019] } };

        var result = await new SearchService(db, s_clock, embedder: embedder).ExecuteAsync(query);

        // Asset 5 is as similar but from 2018; asset 1 is from 2019 but unrelated.
        Assert.Equal([Ids.Of(2), Ids.Of(3)], result.Hits.Select(h => h.Id));
        Assert.All(result.Hits, h => Assert.Equal(2019, h.CapturedAt!.Value.Year));
    }

    [Fact]
    public async Task Leaves_out_assets_below_the_threshold_or_without_an_embedding()
    {
        await using var db = await CatalogWithEmbeddingsAsync();
        var embedder = new FakeQueryEmbedder(embed: _ => FakeQueryEmbedder.Axis(1));
        var service = new SearchService(db, s_clock, embedder: embedder);

        var loose = await service.ExecuteAsync(Visual("snow", minScore: 0));
        var strict = await service.ExecuteAsync(Visual("snow", minScore: 0.9));

        // Asset 1 is orthogonal (similarity 0, kept only at 0); asset 4 has no embedding at all.
        Assert.Equal([Ids.Of(1), Ids.Of(2), Ids.Of(3), Ids.Of(5)], loose.Hits.Select(h => h.Id).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Ids.Of(4), loose.Hits.Select(h => h.Id));
        Assert.Equal([Ids.Of(2), Ids.Of(5)], strict.Hits.Select(h => h.Id).Order(StringComparer.Ordinal));
        // pgvector's cosine distance operator, which the HNSW index (vector_cosine_ops) serves.
        Assert.Contains("<=>", PgvectorSimilarity.Rank(db, db.Assets, FakeQueryEmbedder.Axis(1), 0.2).ToQueryString(), StringComparison.Ordinal);
        // Exact ranking orders by an expression the index cannot serve; the index-friendly order is the bare distance.
        Assert.Contains("+ 0.0", PgvectorSimilarity.Rank(db, db.Assets, FakeQueryEmbedder.Axis(1), 0.2, exact: true).ToQueryString(), StringComparison.Ordinal);
        Assert.DoesNotContain("+ 0.0", PgvectorSimilarity.Rank(db, db.Assets, FakeQueryEmbedder.Axis(1), 0.2).ToQueryString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_a_search_by_description_without_a_text_model_but_runs_the_others()
    {
        await using var db = await CatalogWithEmbeddingsAsync();
        var service = new SearchService(db, s_clock, embedder: new FakeQueryEmbedder(available: false));

        await Assert.ThrowsAsync<VisualSearchUnavailableException>(() => service.ExecuteAsync(Visual("snow")));
        await Assert.ThrowsAsync<VisualSearchUnavailableException>(() => new SearchService(db, s_clock).ExecuteAsync(Visual("snow")));
        Assert.Equal(5, (await service.ExecuteAsync(new SearchQuery())).Hits.Count);
    }

    [Fact]
    public async Task The_api_and_the_page_rank_a_real_catalog_by_description()
    {
        string connection;
        await using (var db = await CatalogWithEmbeddingsAsync())
        {
            connection = catalog.ConnectionStringFor(db);
        }

        await using var web = new WebFactory(connection) { QueryEmbedder = new FakeQueryEmbedder(embed: _ => FakeQueryEmbedder.Axis(1, 1, 2, 0.1f)) };
        using var http = web.CreateSignedInClient();

        using var api = await http.PostAsync(
            new Uri("/api/search", UriKind.Relative),
            new StringContent("""{"filters": {"visual": {"text": "snow"}, "years": [2019]}}""", System.Text.Encoding.UTF8, "application/json"));
        var html = await http.GetStringAsync(new Uri("/?filter=1&text=snow", UriKind.Relative));

        Assert.Equal(System.Net.HttpStatusCode.OK, api.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await api.Content.ReadAsStringAsync());
        Assert.Equal([Ids.Of(2), Ids.Of(3)], body.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
        Assert.True(html.IndexOf($"photos/{Ids.Of(2)}", StringComparison.Ordinal) < html.IndexOf($"photos/{Ids.Of(3)}", StringComparison.Ordinal));
        Assert.Contains("Closest to the description", html, StringComparison.Ordinal);
        Assert.Contains("name=\"text\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Finds_every_filtered_match_even_where_the_index_would_miss_them()
    {
        // 2,500 photos from 2020 close to the description (similarity about 0.8) and 5 from 2019 further away (about
        // 0.4). Walking the HNSW index, even iteratively with no limits, reaches only one of the five: an HNSW graph
        // does not link every outlier from the cluster (reproduced in plain SQL, D-070). So a filtered search ranks
        // its candidates exactly, which finds all five even with the planner pushed towards indexes.
        var random = new Random(16);
        var description = Unit(Gaussian(random));
        var db = await catalog.CreateMigratedDatabaseAsync();
        await using var _ = db;
        for (var n = 1; n <= 2505; n++)
        {
            var near = n > 5;
            db.Assets.Add(Asset(n, new DateTime(near ? 2020 : 2019, 6, 1)));
            db.AssetEmbeddings.Add(Embedding(n, Unit(Mix(description, Gaussian(random), near ? 0.6 : 0.3))));
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlRawAsync("ANALYZE assets; ANALYZE asset_embeddings;");
        // The planner then has to walk the HNSW index in distance order, as it would on a large library.
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET enable_seqscan = off; SET enable_sort = off; SET enable_bitmapscan = off");
        var query = new SearchQuery { Filters = new SearchFilters { Visual = new VisualFilter { Text = "snow" }, Years = [2019] } };

        var result = await new SearchService(db, s_clock, embedder: new FakeQueryEmbedder(embed: _ => description)).ExecuteAsync(query);

        var plan = await ExplainAsync(db, query, description);
        Assert.False(plan.Contains("ix_asset_embeddings_embedding", StringComparison.Ordinal), plan);
        Assert.Equal([Ids.Of(1), Ids.Of(2), Ids.Of(3), Ids.Of(4), Ids.Of(5)], result.Hits.Select(h => h.Id).Order(StringComparer.Ordinal));
    }

    private static float[] Gaussian(Random random) =>
        [.. Enumerable.Range(0, 512).Select(_ => (float)(Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble())))];

    private static float[] Mix(float[] towards, float[] noise, double weight)
    {
        var unitNoise = Unit(noise);
        return [.. towards.Zip(unitNoise, (t, r) => (float)((weight * t) + ((1 - weight) * r)))];
    }

    private static float[] Unit(float[] vector)
    {
        var norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        return [.. vector.Select(v => (float)(v / norm))];
    }

    [Fact]
    public async Task Sets_the_iterative_scan_only_inside_the_search_s_transaction()
    {
        await using var db = await CatalogWithEmbeddingsAsync();
        await db.Database.OpenConnectionAsync();
        var service = new SearchService(db, s_clock, embedder: new FakeQueryEmbedder(embed: _ => FakeQueryEmbedder.Axis(1)));

        await service.ExecuteAsync(Visual("snow"));

        // SET LOCAL ends with the transaction: the pooled connection is left as it was.
        Assert.Equal("off", await db.Database.SqlQuery<string>($"SELECT current_setting('hnsw.iterative_scan') AS \"Value\"").SingleAsync());
        Assert.Equal("40", await db.Database.SqlQuery<string>($"SELECT current_setting('hnsw.ef_search') AS \"Value\"").SingleAsync());
    }

    // The plan of the query RunAsync sends (printed by ToQueryString), with the same settings.
    private static async Task<string> ExplainAsync(CatalogDbContext db, SearchQuery query, float[] description)
    {
        var filtered = new SearchService(db, s_clock).Filtered(query with { Filters = query.Filters with { Visual = null } });
        var sql = PgvectorSimilarity.Rank(db, filtered, description, 0.2, exact: true).Take(201).ToQueryString();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await PgvectorSimilarity.PrepareAsync(db, CancellationToken.None);
        var lines = sql.Split('\n');
        var body = string.Join('\n', lines.SkipWhile(l => l.StartsWith("-- ", StringComparison.Ordinal)));
        var command = (Npgsql.NpgsqlCommand)db.Database.GetDbConnection().CreateCommand();
        await using (command)
        {
            command.Transaction = (Npgsql.NpgsqlTransaction)Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(transaction);
            command.CommandText = "EXPLAIN " + body;
            foreach (var line in lines.TakeWhile(l => l.StartsWith("-- @", StringComparison.Ordinal)))
            {
                var name = line[4..line.IndexOf('=', StringComparison.Ordinal)];
                command.Parameters.AddWithValue(name, name.Contains("vector", StringComparison.OrdinalIgnoreCase) || line.Contains('[', StringComparison.Ordinal)
                    ? new Pgvector.Vector(description)
                    : ParameterValue(line));
            }

            var plan = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }

            return string.Join('\n', plan);
        }
    }

    private static object ParameterValue(string declaration)
    {
        var value = declaration[(declaration.IndexOf('=', StringComparison.Ordinal) + 1)..];
        var type = value.LastIndexOf(" (DbType", StringComparison.Ordinal);
        value = (type < 0 ? value : value[..type]).Trim().Trim('\'');
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return value.StartsWith('{') ? new[] { int.Parse(value.Trim('{', '}', ' ', '\''), culture) }
            : int.TryParse(value, System.Globalization.NumberStyles.Integer, culture, out var whole) ? whole
            : double.TryParse(value, System.Globalization.NumberStyles.Float, culture, out var number) ? number
            : value;
    }

    private static SearchQuery Visual(string text, double? minScore = null) =>
        new() { Filters = new SearchFilters { Visual = new VisualFilter { Text = text, MinScore = minScore } } };

    // Five photos: 1 (2019) on axis 0, 2 (2019) on axis 1, 3 (2019) mostly axis 1, 4 (2019) without an embedding,
    // 5 (2018) on axis 1 with a little of axis 3.
    private async Task<CatalogDbContext> CatalogWithEmbeddingsAsync()
    {
        var db = await CatalogWithAsync(
            Asset(1, new DateTime(2019, 1, 1)), Asset(2, new DateTime(2019, 2, 1)), Asset(3, new DateTime(2019, 3, 1)),
            Asset(4, new DateTime(2019, 4, 1)), Asset(5, new DateTime(2018, 5, 1)));
        db.AssetEmbeddings.AddRange(
            Embedding(1, FakeQueryEmbedder.Axis(0)),
            Embedding(2, FakeQueryEmbedder.Axis(1)),
            Embedding(3, FakeQueryEmbedder.Axis(1, 0.8f, 2, 0.6f)),
            Embedding(5, FakeQueryEmbedder.Axis(1, 0.95f, 3, 0.31f)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static AssetEmbedding Embedding(int n, float[] vector) =>
        new() { AssetId = Ids.Of(n), Model = PgvectorSimilarity.ModelKey, Embedding = new Pgvector.Vector(vector) };

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
