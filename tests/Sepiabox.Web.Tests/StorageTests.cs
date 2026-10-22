using System.Globalization;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Components.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The storage DSL (S-028, D-093): <c>aggregate.sum</c> and <c>filters.sourceCount</c>, parsed, validated, and shown as chips.</summary>
public sealed class StorageDslTests
{
    private static readonly Lazy<JsonSchema> s_schema = new(() =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(SourceTree.Root, "docs", "schemas", "search.v1.json"))));

    [Theory]
    [InlineData("""{"filters":{"sourceCount":{"eq":1}}}""", null)]
    [InlineData("""{"filters":{"sourceCount":{"gte":2}}}""", null)]
    [InlineData("""{"filters":{"kinds":["video"],"years":[2019]},"aggregate":{"count":"assets","sum":"bytes"}}""", null)]
    [InlineData("""{"filters":{},"aggregate":{"count":"assets","groupBy":"year","sum":"bytes"}}""", null)]
    [InlineData("""{"filters":{"sourceCount":{}}}""", "filters.sourceCount needs exactly one of eq or gte.")]
    [InlineData("""{"filters":{"sourceCount":{"eq":1,"gte":2}}}""", "filters.sourceCount needs exactly one of eq or gte.")]
    [InlineData("""{"filters":{"sourceCount":{"eq":0}}}""", "filters.sourceCount.eq and gte must be between 1 and 1000.")]
    [InlineData("""{"filters":{"sourceCount":{"gte":1001}}}""", "filters.sourceCount.eq and gte must be between 1 and 1000.")]
    [InlineData("""{"filters":{"sourceCount":{"eq":null}}}""", "filters.sourceCount needs exactly one of eq or gte.")]
    [InlineData("""{"filters":{"sourceCount":{"lte":2}}}""", "The search is not valid search DSL v1 JSON at $.filters.sourceCount.lte: an unknown or repeated field, a null, a value of the wrong type, or malformed JSON.")]
    [InlineData("""{"filters":{},"aggregate":{"count":"assets","sum":"duration"}}""", "aggregate.sum must be 'bytes'.")]
    [InlineData("""{"filters":{},"aggregate":{"count":"assets","groupBy":"person","sum":"bytes"}}""", "aggregate.sum cannot be added up per person: a photo with several people has one size.")]
    public void Validates_the_source_count_and_the_sum(string json, string? expected)
    {
        var valid = SearchQueryJson.TryParse(json, out var query, out var error);

        Assert.Equal(expected is null, valid);
        if (expected is null)
        {
            Assert.True(SearchQueryJson.TryParse(SearchQueryJson.Serialize(query), out var again, out _));
            Assert.Equal(query, again);
            Assert.True(SearchQueryJson.TryDecode(SearchQueryJson.Encode(query), out var decoded, out _));
            Assert.Equal(query, decoded);
        }
        else
        {
            Assert.Equal(expected, error);
        }
    }

    [Theory]
    [InlineData("""{"filters":{"sourceCount":{"eq":1}}}""", true)]
    [InlineData("""{"filters":{"sourceCount":{"gte":2}}}""", true)]
    [InlineData("""{"filters":{},"aggregate":{"count":"assets","sum":"bytes"}}""", true)]
    [InlineData("""{"filters":{"sourceCount":{"eq":0}}}""", false)]
    [InlineData("""{"filters":{"sourceCount":{"lte":2}}}""", false)]
    [InlineData("""{"filters":{},"aggregate":{"count":"assets","sum":"duration"}}""", false)]
    public void The_schema_file_agrees_on_the_new_fields(string json, bool valid)
    {
        var result = s_schema.Value.Evaluate(JsonNode.Parse(json), new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Accepts_the_storage_fields_in_a_translated_answer_since_the_translator_writes_them()
    {
        Assert.Null(QuestionService.Validate("""{"filters":{"sourceCount":{"eq":1}}}"""));
        Assert.Null(QuestionService.Validate("""{"filters":{"sourceCount":{"gte":2},"kinds":["photo"]},"aggregate":{"count":"assets"}}"""));
        Assert.Null(QuestionService.Validate("""{"filters":{},"aggregate":{"count":"assets","sum":"bytes"}}"""));
        Assert.Null(QuestionService.Validate("""{"filters":{"years":[2019]},"aggregate":{"count":"assets"}}"""));
        // Still the DSL's own rules: exactly one of eq and gte, and no size per person.
        Assert.NotNull(QuestionService.Validate("""{"filters":{"sourceCount":{"eq":1,"gte":2}}}"""));
        Assert.NotNull(QuestionService.Validate("""{"filters":{},"aggregate":{"count":"assets","groupBy":"person","sum":"bytes"}}"""));
        // What the output format leaves out is still refused.
        Assert.NotNull(QuestionService.Validate("""{"filters":{"quality":{"blurry":true}}}"""));
    }

    [Fact]
    public void The_filter_bar_carries_the_source_count_through_to_the_next_search()
    {
        var narrowed = SearchLinks.FromFields("2019", null, null, null, null, sourceEq: "1");
        var atLeast = SearchLinks.FromFields(null, null, "video", null, null, sourceGte: "2");

        Assert.Equal(new SourceCountFilter { Eq = 1 }, narrowed.Filters.SourceCount);
        Assert.Equal([2019], narrowed.Filters.Years!);
        Assert.Equal(new SourceCountFilter { Gte = 2 }, atLeast.Filters.SourceCount);
        Assert.Equal(new SourceCountFilter { Eq = 1 }, SearchLinks.ToFields(narrowed).SourceCount);
        // Hand-edited addresses: nothing that the DSL would refuse, and the exact count wins over "at least".
        Assert.Null(SearchLinks.FromFields(null, null, null, null, null, sourceEq: "0", sourceGte: "x").Filters.SourceCount);
        Assert.Null(SearchLinks.FromFields(null, null, null, null, null, sourceEq: "1001").Filters.SourceCount);
        Assert.Equal(new SourceCountFilter { Eq = 3 }, SearchLinks.FromFields(null, null, null, null, null, sourceEq: "3", sourceGte: "2").Filters.SourceCount);
        Assert.Null(SearchLinks.FromFields(null, null, null, null, null).Filters.SourceCount);
    }

    [Fact]
    public async Task Picking_a_year_on_a_source_count_search_keeps_the_source_count()
    {
        var library = new FakeLibrary();
        await using var web = new WebFactory(library);
        using var http = web.CreateSignedInClient(followRedirects: false);
        var shown = SearchLinks.Timeline(new SearchQuery { Filters = new SearchFilters { SourceCount = new SourceCountFilter { Eq = 1 } } });

        // The bar on the page shows the filter in hidden fields...
        var html = await http.GetStringAsync(new Uri(shown, UriKind.Relative));
        Assert.Contains("name=\"source-eq\" value=\"1\"", html, StringComparison.Ordinal);

        // ...so the form it sends, with a year picked, comes back to a search with both.
        using var response = await http.GetAsync(new Uri("/?filter=1&source-eq=1&year=2019&month=&kind=&city=&source=", UriKind.Relative));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.True(SearchQueryJson.TryDecode(location[(location.IndexOf("q=", StringComparison.Ordinal) + 2)..], out var query, out _));
        Assert.Equal(new SourceCountFilter { Eq = 1 }, query.Filters.SourceCount);
        Assert.Equal([2019], query.Filters.Years!);
    }

    [Fact]
    public void Queries_with_different_source_counts_are_different()
    {
        var one = new SearchFilters { SourceCount = new SourceCountFilter { Eq = 1 } };

        Assert.NotEqual(one, new SearchFilters { SourceCount = new SourceCountFilter { Gte = 1 } });
        Assert.Equal(one, new SearchFilters { SourceCount = new SourceCountFilter { Eq = 1 } });
        Assert.False(one.IsEmpty);
    }

    [Theory]
    [InlineData(Cultures.English, 1, null, "In only one source")]
    [InlineData(Cultures.English, 3, null, "In 3 sources")]
    [InlineData(Cultures.English, null, 2, "In 2 or more sources")]
    [InlineData(Cultures.BrazilianPortuguese, 1, null, "Em apenas uma origem")]
    [InlineData(Cultures.BrazilianPortuguese, 3, null, "Em 3 origens")]
    [InlineData(Cultures.BrazilianPortuguese, null, 2, "Em 2 ou mais origens")]
    public void Shows_the_source_count_as_a_removable_chip(string culture, int? eq, int? gte, string label)
    {
        using var language = Cultures.Use(culture);
        var query = new SearchQuery
        {
            Filters = new SearchFilters { Kinds = ["video"], SourceCount = new SourceCountFilter { Eq = eq, Gte = gte } },
        };

        var chips = SearchChips.For(query, Cultures.Text());

        Assert.Equal(label, chips[1].Label);
        Assert.Null(chips[1].Without.Filters.SourceCount);
        Assert.Equal(["video"], chips[1].Without.Filters.Kinds!);
    }

    [Theory]
    [InlineData(Cultures.English, null, "Size of originals")]
    [InlineData(Cultures.English, "year", "Size of originals by year")]
    [InlineData(Cultures.BrazilianPortuguese, "year", "Tamanho dos originais por ano")]
    public void Names_a_size_count_in_its_chip(string culture, string? groupBy, string label)
    {
        using var language = Cultures.Use(culture);
        var query = new SearchQuery { Aggregate = new SearchAggregate { GroupBy = groupBy, Sum = SearchSum.Bytes } };

        Assert.Equal(label, Assert.Single(SearchChips.For(query, Cultures.Text())).Label);
    }
}

public sealed class ByteSizesTests
{
    [Theory]
    [InlineData(Cultures.English, 0L, "0 B")]
    [InlineData(Cultures.English, 1023L, "1,023 B")]
    [InlineData(Cultures.English, 1024L, "1 KB")]
    [InlineData(Cultures.English, 1536L, "1.5 KB")]
    [InlineData(Cultures.English, 5L * 1024 * 1024, "5 MB")]
    [InlineData(Cultures.English, 2_576_980_378L, "2.4 GB")]
    [InlineData(Cultures.BrazilianPortuguese, 2_576_980_378L, "2,4 GB")]
    [InlineData(Cultures.English, 3L * 1024 * 1024 * 1024 * 1024, "3 TB")]
    [InlineData(Cultures.English, -5L, "0 B")]
    [InlineData(Cultures.English, 1_048_575L, "1 MB")]
    [InlineData(Cultures.English, 1_073_741_823L, "1 GB")]
    [InlineData(Cultures.English, 1_048_064L, "1023.5 KB")]
    public void Formats_a_size_for_people_in_the_current_culture(string culture, long bytes, string expected)
    {
        using var language = Cultures.Use(culture);

        Assert.Equal(expected, ByteSizes.Format(bytes));
    }
}

/// <summary>A size count on the answer card (S-028).</summary>
public sealed class SizeAnswerTests : LocalizedContext
{
    private static readonly SearchQuery Videos2019 = new()
    {
        Filters = new SearchFilters { Kinds = ["video"], Years = [2019] },
        Aggregate = new SearchAggregate { Sum = SearchSum.Bytes },
    };

    [Theory]
    [InlineData(Cultures.English, "2.4 GB", "Space used by 312 videos in 2019")]
    [InlineData(Cultures.BrazilianPortuguese, "2,4 GB", "Espaço ocupado por 312 vídeos em 2019")]
    public void Shows_the_size_as_the_number_and_what_it_is_the_size_of(string culture, string number, string sentence)
    {
        using var language = Cultures.Use(culture);

        var card = Render<AnswerCard>(p => p.Add(c => c.Query, Videos2019).Add(c => c.Count, new CountResult(312, [], null) { Bytes = 2_576_980_378L }));

        Assert.Equal(number, card.Find(".number").TextContent);
        Assert.Equal(sentence, card.Find(".sentence").TextContent);
    }

    [Fact]
    public void Adds_a_size_column_to_a_ranking_only_when_sizes_were_asked_for()
    {
        var query = Videos2019 with { Aggregate = new SearchAggregate { GroupBy = SearchGroupBy.Year, Sum = SearchSum.Bytes } };
        var count = new CountResult(5, [new CountGroup("2019", 3, 3000), new CountGroup("2020", 2, 2048)], null) { Bytes = 5048 };

        var sized = Render<AnswerCard>(p => p.Add(c => c.Query, query).Add(c => c.Count, count));
        var plain = Render<AnswerCard>(p => p
            .Add(c => c.Query, query with { Aggregate = new SearchAggregate { GroupBy = SearchGroupBy.Year } })
            .Add(c => c.Count, new CountResult(5, [new CountGroup("2019", 3), new CountGroup("2020", 2)], null)));

        Assert.Equal(["Group", "Count", "Size of originals"], sized.FindAll("thead th").Select(h => h.TextContent));
        Assert.Equal(["2019", "3", "2.9 KB"], sized.FindAll("tbody tr")[0].Children.Select(c => c.TextContent));
        Assert.Equal(["2020", "2", "2 KB"], sized.FindAll("tbody tr")[1].Children.Select(c => c.TextContent));
        Assert.Equal(["Group", "Count"], plain.FindAll("thead th").Select(h => h.TextContent));
    }
}

/// <summary>The storage page (S-028) on a fake library.</summary>
public sealed class StoragePageTests : LocalizedContext
{
    private static readonly StorageReport Report = new(
        [
            new StorageRow("photo", 2019, 12, 12_000),
            new StorageRow("video", 2019, 2, 8 * 1024 * 1024),
            new StorageRow("video", 2020, 1, 3 * 1024 * 1024),
            new StorageRow("photo", null, 4, 4_000),
        ],
        OneSource: 15,
        OneSourceBytes: 20_000,
        SeveralSources: 4,
        SeveralSourcesBytes: 5_000,
        InSeveralSources: [new StorageAsset(Ids.Of(1), "photo", new DateTime(2019, 7, 14, 10, 0, 0), null, 1_000, true, ["HD da Ana", "Takeout 2026"])],
        LargestVideos: [new StorageAsset(Ids.Of(2), "video", new DateTime(2019, 12, 24, 20, 0, 0), 62, 8 * 1024 * 1024, true, ["HD da Ana"])]);

    [Fact]
    public void Shows_the_totals_by_kind_and_year_newest_first_with_the_undated_last()
    {
        var page = RenderPage(Report);

        Assert.Equal(
            ["Year", "Photos", "Photos size", "Videos", "Videos size"],
            page.FindAll(".storage-totals thead th").Select(h => h.TextContent));
        Assert.Equal("storage-totals-heading", page.Find(".storage-totals").GetAttribute("aria-labelledby"));
        Assert.Equal("Photos and videos from 2019", page.FindAll(".storage-totals tbody a").Single(a => a.TextContent == "2019").GetAttribute("aria-label"));
        var rows = page.FindAll(".storage-totals tbody tr").Select(r => r.Children.Select(c => c.TextContent.Trim()).ToArray()).ToList();
        Assert.Equal(["2020", "0", "–", "1", "3 MB"], rows[0]);
        Assert.Equal(["2019", "12", "11.7 KB", "2", "8 MB"], rows[1]);
        Assert.Equal(["No date", "4", "3.9 KB", "0", "–"], rows[2]);
        var total = page.Find(".storage-totals tfoot tr").Children.Select(c => c.TextContent.Trim()).ToArray();
        Assert.Equal(["Total", "16", "15.6 KB", "3", "11 MB"], total);
        Assert.Contains(page.FindAll(".storage-totals tbody a"), a => a.TextContent == "2019" && a.GetAttribute("href")!.StartsWith("./?q=", StringComparison.Ordinal));
    }

    [Fact]
    public void Shows_the_assets_in_one_source_and_in_several_with_their_source_names_and_links()
    {
        var page = RenderPage(Report);

        var one = page.Find(".storage-one-source");
        Assert.Contains("15 items, 19.5 KB", one.TextContent, StringComparison.Ordinal);
        Assert.Equal("Show these items", one.QuerySelector("a")!.TextContent);
        Assert.Equal("Show these items, only in one source", one.QuerySelector("a")!.GetAttribute("aria-label"));
        var oneLink = one.QuerySelector("a")!.GetAttribute("href")!;
        Assert.True(SearchQueryJson.TryDecode(oneLink["./?q=".Length..], out var oneQuery, out _));
        Assert.Equal(new SourceCountFilter { Eq = 1 }, oneQuery.Filters.SourceCount);

        var several = page.Find(".storage-several-sources");
        Assert.Contains("4 items, 4.9 KB", several.TextContent, StringComparison.Ordinal);
        Assert.True(SearchQueryJson.TryDecode(several.QuerySelector("p a")!.GetAttribute("href")!["./?q=".Length..], out var severalQuery, out _));
        Assert.Equal(new SourceCountFilter { Gte = 2 }, severalQuery.Filters.SourceCount);
        Assert.Equal("In: HD da Ana, Takeout 2026", several.QuerySelector(".storage-assets p")!.TextContent);
        Assert.Equal("HD da Ana, Takeout 2026", several.QuerySelector(".storage-assets p")!.GetAttribute("title"));
        Assert.Equal("Show these items, in several sources", several.QuerySelector("p a")!.GetAttribute("aria-label"));
        Assert.Equal($"photos/{Ids.Of(1)}", several.QuerySelector(".storage-assets a.thumb")!.GetAttribute("href"));
        Assert.Contains("Showing the first 1 items.", several.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Lists_the_largest_videos_with_their_sizes()
    {
        var page = RenderPage(Report);

        var largest = page.Find(".storage-largest");
        Assert.Equal("The 20 largest videos", largest.QuerySelector("h2")!.TextContent);
        Assert.Equal("8 MB", largest.QuerySelector(".size")!.TextContent);
        Assert.Equal($"photos/{Ids.Of(2)}", largest.QuerySelector("a.thumb")!.GetAttribute("href"));
    }

    [Fact]
    public void Says_so_when_nothing_is_in_one_source_or_in_several_and_when_there_are_no_videos()
    {
        var page = RenderPage(new StorageReport([new StorageRow("photo", 2019, 1, 100)], 0, 0, 0, 0, [], []));

        Assert.Equal("No items are only in one source.", page.Find(".storage-one-source p").TextContent);
        Assert.Equal("No items are in several sources.", page.Find(".storage-several-sources p").TextContent);
        Assert.Equal("There are no videos yet.", page.Find(".storage-largest p").TextContent);
    }

    [Fact]
    public void Says_so_for_an_empty_library()
    {
        var page = RenderPage(new StorageReport([], 0, 0, 0, 0, [], []));

        Assert.Equal("The library has no photos or videos yet.", page.Find("p.empty").TextContent);
        Assert.Empty(page.FindAll("table"));
    }

    [Fact]
    public void Is_in_brazilian_portuguese_with_its_number_formats()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);

        var page = RenderPage(Report);

        Assert.Equal("Armazenamento", page.Find("h1").TextContent);
        Assert.Equal("Totais por tipo e ano", page.Find(".storage-totals h2").TextContent);
        Assert.Equal("Em apenas uma origem", page.Find(".storage-one-source h2").TextContent);
        Assert.Equal(["Ano", "Fotos", "Tamanho das fotos", "Vídeos", "Tamanho dos vídeos"], page.FindAll(".storage-totals thead th").Select(h => h.TextContent));
        Assert.Equal("Mostrar esses itens", page.Find(".storage-one-source a").TextContent);
        Assert.Equal("Mostrar esses itens, em apenas uma origem", page.Find(".storage-one-source a").GetAttribute("aria-label"));
        Assert.Contains("15 itens, 19,5 KB", page.Find(".storage-one-source").TextContent, StringComparison.Ordinal);
        Assert.Equal("Em: HD da Ana, Takeout 2026", page.Find(".storage-several-sources .storage-assets p").TextContent);
        Assert.Equal("Os 20 maiores vídeos", page.Find(".storage-largest h2").TextContent);
        Assert.Equal("Sem data", page.Find(".storage-totals tbody tr:last-child th").TextContent.Trim());
    }

    private IRenderedComponent<Web.Components.Pages.Storage> RenderPage(StorageReport report)
    {
        Services.AddSingleton<ILibraryQueries>(new FakeLibrary { Storage = report });
        return Render<Web.Components.Pages.Storage>();
    }
}

/// <summary>The storage page and the DSL's new fields on a real PostgreSQL catalog (S-028).</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class StorageCatalogTests(CatalogFixture catalog)
{
    private static readonly FixedTime s_clock = new(new DateTimeOffset(2026, 10, 19, 9, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task Filters_by_the_number_of_different_sources()
    {
        await using var db = await SeededAsync();

        // 1: one source; 2: two; 3: the same source twice (one); 4: three; 5: one; 6 (video): one; 7 (video): two; 8 (video): one.
        Assert.Equal([1, 3, 5, 6, 8], await IdsAsync(db, new SearchFilters { SourceCount = new SourceCountFilter { Eq = 1 } }));
        Assert.Equal([2, 4, 7], await IdsAsync(db, new SearchFilters { SourceCount = new SourceCountFilter { Gte = 2 } }));
        Assert.Equal([4], await IdsAsync(db, new SearchFilters { SourceCount = new SourceCountFilter { Eq = 3 } }));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], await IdsAsync(db, new SearchFilters { SourceCount = new SourceCountFilter { Gte = 1 } }));
        Assert.Empty(await IdsAsync(db, new SearchFilters { SourceCount = new SourceCountFilter { Gte = 4 } }));
    }

    [Fact]
    public async Task Combines_the_source_count_with_the_other_filters()
    {
        await using var db = await SeededAsync();

        Assert.Equal([7], await IdsAsync(db, new SearchFilters { Kinds = ["video"], SourceCount = new SourceCountFilter { Gte = 2 } }));
        Assert.Equal([2, 7], await IdsAsync(db, new SearchFilters { Years = [2019], SourceCount = new SourceCountFilter { Eq = 2 } }));
    }

    [Fact]
    public async Task Adds_up_the_sizes_of_the_videos_of_2019()
    {
        await using var db = await SeededAsync();
        var query = new SearchQuery
        {
            Filters = new SearchFilters { Kinds = ["video"], Years = [2019] },
            Aggregate = new SearchAggregate { Sum = SearchSum.Bytes },
        };

        var count = await new SearchService(db, s_clock).CountAsync(query);

        Assert.Equal(2, count!.Total);
        Assert.Equal(8_000, count.Bytes);
    }

    [Fact]
    public async Task Gives_no_size_unless_asked_and_zero_for_nothing_found()
    {
        await using var db = await SeededAsync();
        var service = new SearchService(db, s_clock);

        var plain = await service.CountAsync(new SearchQuery { Aggregate = new SearchAggregate { GroupBy = SearchGroupBy.Kind } });
        var empty = await service.CountAsync(new SearchQuery
        {
            Filters = new SearchFilters { Years = [1999] },
            Aggregate = new SearchAggregate { Sum = SearchSum.Bytes },
        });

        Assert.Null(plain!.Bytes);
        Assert.All(plain.Groups, g => Assert.Null(g.Bytes));
        Assert.Equal((0, 0L), (empty!.Total, empty.Bytes));
    }

    [Fact]
    public async Task Ranks_sources_and_cities_by_size_when_sizes_are_asked_for()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.AddRange(
            AssetIn(1, 10_000, "Big", city: "Porto"),
            AssetIn(2, 100, "Small", city: "Lisbon"),
            AssetIn(3, 100, "Small", city: "Lisbon"),
            AssetIn(4, 100, "Small", city: "Lisbon"));
        await db.SaveChangesAsync();
        var service = new SearchService(db, s_clock);
        SearchQuery By(string groupBy, string? sum) => new() { Aggregate = new SearchAggregate { GroupBy = groupBy, Top = 1, Sum = sum } };

        var bigSource = await service.CountAsync(By(SearchGroupBy.Source, SearchSum.Bytes));
        var bigCity = await service.CountAsync(By(SearchGroupBy.City, SearchSum.Bytes));
        var mostItemsSource = await service.CountAsync(By(SearchGroupBy.Source, null));
        var all = await service.CountAsync(new SearchQuery { Aggregate = new SearchAggregate { GroupBy = SearchGroupBy.Source, Sum = SearchSum.Bytes } });

        // The group that takes up the most, though it has fewer items; by count, as before, when no size was asked for.
        Assert.Equal(["Big:1:10000"], bigSource!.Groups.Select(g => $"{g.Key}:{g.Count}:{g.Bytes}"));
        Assert.Equal(["Porto:1:10000"], bigCity!.Groups.Select(g => $"{g.Key}:{g.Count}:{g.Bytes}"));
        Assert.Equal(["Small"], mostItemsSource!.Groups.Select(g => g.Key));
        Assert.Equal(["Big", "Small"], all!.Groups.Select(g => g.Key));
        Assert.Equal(2, all.AllGroups);
    }

    [Fact]
    public async Task Adds_up_sizes_per_group()
    {
        await using var db = await SeededAsync();
        var service = new SearchService(db, s_clock);

        var byYear = await service.CountAsync(new SearchQuery { Aggregate = new SearchAggregate { GroupBy = SearchGroupBy.Year, Sum = SearchSum.Bytes } });
        var byKind = await service.CountAsync(new SearchQuery { Aggregate = new SearchAggregate { GroupBy = SearchGroupBy.Kind, Sum = SearchSum.Bytes } });
        var bySource = await service.CountAsync(new SearchQuery { Aggregate = new SearchAggregate { GroupBy = SearchGroupBy.Source, Sum = SearchSum.Bytes } });

        Assert.Equal(["2019:6:9700", "2020:1:7000", ":1:50"], byYear!.Groups.Select(g => $"{g.Key}:{g.Count}:{g.Bytes}"));
        Assert.Equal(16_750, byYear.Bytes);
        Assert.Equal(["photo:5:1750", "video:3:15000"], byKind!.Groups.Select(g => $"{g.Key}:{g.Count}:{g.Bytes}"));
        // An asset with several sources counts in each with its whole size; one source twice counts once.
        Assert.Equal(
            ["HD:8:16750", "Takeout:3:6200", "iCloud:1:1000"],
            bySource!.Groups.OrderByDescending(g => g.Count).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}:{g.Count}:{g.Bytes}"));
    }

    [Fact]
    public async Task Reports_the_library_s_storage()
    {
        await using var db = await SeededAsync();

        var report = await new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)), s_clock).GetStorageAsync();

        Assert.Equal(
            ["photo:2019:4:1700", "photo::1:50", "video:2019:2:8000", "video:2020:1:7000"],
            report.Rows.OrderBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.Year is null).ThenBy(r => r.Year)
                .Select(r => $"{r.Kind}:{r.Year}:{r.Count}:{r.Bytes}"));
        Assert.Equal((5, 10_550L), (report.OneSource, report.OneSourceBytes));
        Assert.Equal((3, 6_200L), (report.SeveralSources, report.SeveralSourcesBytes));
        Assert.Equal([Ids.Of(7), Ids.Of(4), Ids.Of(2)], report.InSeveralSources.Select(a => a.Id));
        Assert.Equal(["HD", "Takeout"], report.InSeveralSources.Single(a => a.Id == Ids.Of(2)).Sources);
        Assert.Equal(["HD", "iCloud", "Takeout"], report.InSeveralSources.Single(a => a.Id == Ids.Of(4)).Sources);
        Assert.Equal([Ids.Of(8), Ids.Of(7), Ids.Of(6)], report.LargestVideos.Select(a => a.Id));
        Assert.Equal(7_000, report.LargestVideos[0].Bytes);
        Assert.True(report.LargestVideos[0].HasThumbnail);
    }

    [Fact]
    public async Task Lists_at_most_twenty_of_the_largest_videos_and_fifty_in_several_sources()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.AddRange(Enumerable.Range(1, 51).Select(n => Asset(n, "video", new DateTime(2020, 1, 1).AddDays(n), n * 10, "HD", "Takeout")));
        await db.SaveChangesAsync();

        var report = await new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)), s_clock).GetStorageAsync();

        Assert.Equal(LibraryQueries.LargestVideosLength, report.LargestVideos.Count);
        Assert.Equal(510, report.LargestVideos[0].Bytes);
        Assert.Equal(Enumerable.Range(32, 20).Reverse().Select(n => n * 10L), report.LargestVideos.Select(v => v.Bytes));
        Assert.Equal(LibraryQueries.StorageListLength, report.InSeveralSources.Count);
        Assert.Equal(51, report.SeveralSources);
        Assert.Equal(0, report.OneSource);
    }

    [Fact]
    public async Task Reports_an_empty_library()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();

        var report = await new LibraryQueries(new TestContexts(catalog.ConnectionStringFor(db)), s_clock).GetStorageAsync();

        Assert.Empty(report.Rows);
        Assert.Equal((0, 0L, 0, 0L), (report.OneSource, report.OneSourceBytes, report.SeveralSources, report.SeveralSourcesBytes));
        Assert.Empty(report.InSeveralSources);
        Assert.Empty(report.LargestVideos);
    }

    // Photos 2019: 1 (100 B, HD), 2 (200 B, HD and Takeout), 3 (400 B, HD twice), 4 (1,000 B, three sources); 5, undated
    // (50 B, HD). Videos: 6 (3,000 B, 2019, HD), 7 (5,000 B, 2019, HD and Takeout), 8 (7,000 B, 2020, HD).
    private async Task<CatalogDbContext> SeededAsync()
    {
        var db = await catalog.CreateMigratedDatabaseAsync();
        db.Assets.AddRange(
            Asset(1, "photo", new DateTime(2019, 3, 1), 100, "HD"),
            Asset(2, "photo", new DateTime(2019, 4, 1), 200, "HD", "Takeout"),
            Asset(3, "photo", new DateTime(2019, 5, 1), 400, "HD", "HD"),
            Asset(4, "photo", new DateTime(2019, 6, 1), 1_000, "HD", "Takeout", "iCloud"),
            Asset(5, "photo", null, 50, "HD"),
            Asset(6, "video", new DateTime(2019, 7, 1), 3_000, "HD"),
            Asset(7, "video", new DateTime(2019, 8, 1), 5_000, "HD", "Takeout"),
            Asset(8, "video", new DateTime(2020, 1, 1), 7_000, "HD"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static async Task<List<int>> IdsAsync(CatalogDbContext db, SearchFilters filters)
    {
        var result = await new SearchService(db, s_clock).ExecuteAsync(new SearchQuery { Filters = filters, Sort = SearchSort.CapturedAtAsc });
        return [.. result.Hits.Select(h => int.Parse(h.Id[^8..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)).Order()];
    }

    private static Asset AssetIn(int n, long bytes, string source, string city)
    {
        var asset = Asset(n, "photo", new DateTime(2019, 1, 1), bytes, source);
        asset.PlaceCity = city;
        asset.PlaceCountry = "Portugal";
        return asset;
    }

    private static Asset Asset(int n, string kind, DateTime? capturedAt, long bytes, params string[] sources) => new()
    {
        Id = Ids.Of(n),
        Kind = kind,
        ContentType = kind == "video" ? "video/mp4" : "image/jpeg",
        SizeBytes = bytes,
        CapturedAt = capturedAt,
        ImportedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
        Sources = [.. sources.Select((name, i) => new AssetSource { AssetId = Ids.Of(n), Type = "local", Name = name, Path = $"{n}-{i}.dat" })],
        Derivatives = [new Derivative { AssetId = Ids.Of(n), Name = "thumb-256", BlobPath = $"{Ids.Of(n)}/thumb-256.webp", Width = 256, Height = 192, ContentType = "image/webp" }],
    };
}
