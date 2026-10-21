using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Components.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>Counting answers (S-026): the card's number, sentence and ranking in both languages.</summary>
public sealed class CountAnswerTests : LocalizedContext
{
    private static readonly SearchQuery Videos2019 = new()
    {
        Filters = new SearchFilters { Kinds = ["video"], Years = [2019] },
        Aggregate = new SearchAggregate(),
    };

    [Theory]
    [InlineData(Cultures.English, "4 videos in 2019")]
    [InlineData(Cultures.BrazilianPortuguese, "4 vídeos em 2019")]
    public void Shows_the_number_and_the_sentence_in_the_ui_culture(string culture, string sentence)
    {
        using var language = Cultures.Use(culture);

        var card = Render<AnswerCard>(p => p.Add(c => c.Query, Videos2019).Add(c => c.Count, new CountResult(4, [], null)));

        Assert.Equal("4", card.Find(".number").TextContent);
        Assert.Equal(sentence, card.Find(".sentence").TextContent);
        Assert.Empty(card.FindAll(".undated"));
    }

    [Theory]
    [InlineData(Cultures.English, "3 items have no date and cannot match the dates asked.")]
    [InlineData(Cultures.BrazilianPortuguese, "3 itens não têm data e por isso ficam fora desta contagem.")]
    public void Says_how_many_items_have_no_date_next_to_the_number_with_a_link(string culture, string note)
    {
        using var language = Cultures.Use(culture);

        var card = Render<AnswerCard>(p => p.Add(c => c.Query, Videos2019).Add(c => c.Count, new CountResult(4, [], 3)));

        var undated = card.Find(".undated");
        Assert.StartsWith(note, undated.TextContent.Trim(), StringComparison.Ordinal);
        var link = undated.QuerySelector("a")!.GetAttribute("href")!;
        Assert.True(SearchQueryJson.TryDecode(link[(link.IndexOf("q=", StringComparison.Ordinal) + 2)..], out var items, out _));
        Assert.Equal(new SearchFilters { Kinds = ["video"], UndatedOnly = true }, items.Filters);
        Assert.Null(items.Aggregate);
    }

    [Theory]
    [InlineData(Cultures.English, "1 photo", "1 item in Lisbon", "12 videos in December 2019", "2 photos on this day")]
    [InlineData(Cultures.BrazilianPortuguese, "1 foto", "1 item em Lisbon", "12 vídeos em dezembro de 2019", "2 fotos neste dia")]
    public void Writes_singular_and_plural_and_the_year_month_place_or_day(string culture, string one, string place, string month, string onThisDay)
    {
        using var language = Cultures.Use(culture);
        var text = Cultures.Text();

        Assert.Equal(one, CountAnswers.Sentence(Query(new SearchFilters { Kinds = ["photo"] }), 1, text));
        Assert.Equal(place, CountAnswers.Sentence(Query(new SearchFilters { Places = new PlaceFilter { Cities = ["Lisbon"] } }), 1, text));
        Assert.Equal(month, CountAnswers.Sentence(Query(new SearchFilters { Kinds = ["video"], Years = [2019], Months = [12] }), 12, text));
        Assert.Equal(onThisDay, CountAnswers.Sentence(Query(new SearchFilters { Kinds = ["photo"], OnThisDay = new OnThisDayFilter() }), 2, text));
    }

    [Theory]
    [InlineData(Cultures.English, "3,214 items without a date", "12 photos in HD da Ana")]
    [InlineData(Cultures.BrazilianPortuguese, "3.214 itens sem data", "12 fotos em HD da Ana")]
    public void Says_without_a_date_and_the_source_when_the_count_has_them(string culture, string undated, string source)
    {
        using var language = Cultures.Use(culture);
        var text = Cultures.Text();

        Assert.Equal(undated, CountAnswers.Sentence(Query(new SearchFilters { UndatedOnly = true }), 3214, text));
        Assert.Equal(source, CountAnswers.Sentence(Query(new SearchFilters { Kinds = ["photo"], Sources = ["HD da Ana"] }), 12, text));
    }

    [Theory]
    [InlineData(Cultures.English, "Photos by year in Lisbon", "No date")]
    [InlineData(Cultures.BrazilianPortuguese, "Fotos por ano em Lisbon", "Sem data")]
    public void Shows_a_ranking_with_a_link_to_each_group_s_items(string culture, string heading, string noDate)
    {
        using var language = Cultures.Use(culture);
        var query = new SearchQuery
        {
            Filters = new SearchFilters { Kinds = ["photo"], Places = new PlaceFilter { Cities = ["Lisbon"] } },
            Aggregate = new SearchAggregate { GroupBy = SearchGroupBy.Year, Top = 3 },
        };

        var card = Render<AnswerCard>(p => p.Add(c => c.Query, query)
            .Add(c => c.Count, new CountResult(10, [new CountGroup("2017", 4), new CountGroup("2018", 3), new CountGroup(null, 2)], null)));

        Assert.Equal(heading, card.Find("h2").TextContent);
        var rows = card.FindAll("tbody tr");
        Assert.Equal(["2017", "2018", noDate], rows.Select(r => r.QuerySelector("th")!.TextContent.Trim()));
        Assert.Equal(["4", "3", "2"], rows.Select(r => r.QuerySelector("td")!.TextContent));
        var link = rows[0].QuerySelector("a")!.GetAttribute("href")!;
        Assert.True(SearchQueryJson.TryDecode(link[(link.IndexOf("q=", StringComparison.Ordinal) + 2)..], out var year, out _));
        Assert.Equal([2017], year.Filters.Years!);
        Assert.Equal(["Lisbon"], year.Filters.Places!.Cities!);
        Assert.Null(year.Aggregate);
    }

    [Theory]
    [InlineData(SearchGroupBy.Month, "December")]
    [InlineData(SearchGroupBy.Kind, "Videos")]
    [InlineData(SearchGroupBy.Source, "HD da Ana")]
    public void Labels_months_kinds_and_sources_in_the_ranking(string groupBy, string label)
    {
        var key = groupBy switch { SearchGroupBy.Month => "12", SearchGroupBy.Kind => "video", _ => "HD da Ana" };
        var query = new SearchQuery { Aggregate = new SearchAggregate { GroupBy = groupBy } };

        var row = Assert.Single(CountAnswers.Rows(query, new CountResult(5, [new CountGroup(key, 5)], null), Cultures.Text()));

        Assert.Equal(label, row.Label);
        Assert.NotNull(row.Items);
    }

    [Fact]
    public void Shows_a_count_as_a_chip_that_goes_back_to_the_list()
    {
        var chips = SearchChips.For(Videos2019 with { Aggregate = new SearchAggregate { GroupBy = SearchGroupBy.City } }, Cultures.Text());

        var count = chips.Single(c => c.Label == "Count by city");
        Assert.Null(count.Without.Aggregate);
        Assert.Equal(Videos2019.Filters, count.Without.Filters);
    }

    private static SearchQuery Query(SearchFilters filters) => new() { Filters = filters, Aggregate = new SearchAggregate() };
}

/// <summary>Counting answers end to end (S-026): a question, the redirect, the card; and the two APIs.</summary>
public sealed partial class CountPageTests
{
    private const string HowManyVideos = """{"filters": {"kinds": ["video"], "years": [2019]}, "aggregate": {"count": "assets"}}""";

    private static readonly TimelineItem Item = new(Ids.Of(1), AssetKinds.Photo, new DateTime(2019, 12, 24), true, null);

    [Theory]
    [InlineData("how many videos did we take in 2019?", "en", "4 videos in 2019")]
    [InlineData("quantos vídeos fizemos em 2019?", "pt-BR", "4 vídeos em 2019")]
    public async Task Answers_a_counting_question_with_the_number_on_a_card(string question, string language, string sentence)
    {
        var library = new FakeLibrary(Item) { CountResult = new CountResult(4, [], 0) };
        await using var web = Web(library, new FakeTranslator { Json = HowManyVideos });
        using var http = web.CreateSignedInClient(followRedirects: false);
        http.DefaultRequestHeaders.Add("Accept-Language", language);

        using var posted = await PostQuestionAsync(http, question);
        var page = WebUtility.HtmlDecode(await http.GetStringAsync(new Uri(new Uri("http://localhost/"), posted.Headers.Location!)));

        Assert.Contains("<p class=\"number\">4</p>", page, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"sentence\">{sentence}</p>", page, StringComparison.Ordinal);
        var counted = Assert.Single(library.Counts);
        Assert.Equal([2019], counted.Filters.Years!);
        Assert.Empty(library.Searches);
    }

    [Fact]
    public async Task Shows_the_results_and_why_there_is_no_number_for_a_count_with_a_description()
    {
        var query = new SearchQuery { Filters = new SearchFilters { Visual = new VisualFilter { Text = "snow" } }, Aggregate = new SearchAggregate() };
        var library = new FakeLibrary(Item) { CountResult = null, SearchResults = new SearchPage([Item], false) };
        await using var web = new WebFactory(library) { QueryEmbedder = new FakeQueryEmbedder() };
        using var http = web.CreateSignedInClient();

        var page = WebUtility.HtmlDecode(await http.GetStringAsync(new Uri("/?q=" + SearchQueryJson.Encode(query), UriKind.Relative)));

        Assert.Contains(Resource("Count_NeedsDescription"), page, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"number\"", page, StringComparison.Ordinal);
        Assert.Contains("class=\"grid\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_api_answers_a_count_with_its_numbers()
    {
        var library = new FakeLibrary { CountResult = new CountResult(10, [new CountGroup("2017", 4), new CountGroup(null, 2)], 3) };
        await using var web = new WebFactory(library);
        using var http = web.CreateSignedInClient();

        using var response = await http.PostAsync(
            new Uri("/api/search", UriKind.Relative),
            new StringContent("""{"filters": {"years": [2017]}, "aggregate": {"count": "assets", "groupBy": "year"}}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var count = body.RootElement.GetProperty("count");
        Assert.Equal(10, count.GetProperty("total").GetInt32());
        Assert.Equal(3, count.GetProperty("undated").GetInt32());
        Assert.Equal("""[{"key":"2017","count":4},{"key":null,"count":2}]""", count.GetProperty("groups").GetRawText());
        Assert.Empty(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Empty(library.Searches);
    }

    [Fact]
    public async Task Search_api_answers_a_count_with_a_description_with_its_results_and_the_reason()
    {
        var library = new FakeLibrary { CountResult = null, SearchResults = new SearchPage([Item], false) };
        await using var web = new WebFactory(library);
        using var http = web.CreateSignedInClient();

        using var response = await http.PostAsync(
            new Uri("/api/search", UriKind.Relative),
            new StringContent("""{"filters": {"visual": {"text": "snow"}}, "aggregate": {"count": "assets"}}""", Encoding.UTF8, "application/json"));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.TryGetProperty("count", out _));
        Assert.Equal(Resource("Count_NeedsDescription"), body.RootElement.GetProperty("notCounted").GetString());
        Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Questions_api_counts_the_whole_library_for_how_many_items_in_total()
    {
        var library = new FakeLibrary { CountResult = new CountResult(51, [], null) };
        await using var web = Web(library, new FakeTranslator { Json = """{"filters": {}, "aggregate": {"count": "assets"}}""" });
        using var http = web.CreateSignedInClient();

        using var response = await http.PostAsJsonAsync(new Uri("/api/questions", UriKind.Relative), new { question = "how many items in total?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(51, body.RootElement.GetProperty("count").GetProperty("total").GetInt32());
        Assert.Equal("assets", body.RootElement.GetProperty("query").GetProperty("aggregate").GetProperty("count").GetString());
    }

    private static WebFactory Web(FakeLibrary library, FakeTranslator translator) => new(library) { QuestionTranslator = translator };

    private static async Task<HttpResponseMessage> PostQuestionAsync(HttpClient http, string question)
    {
        var page = await http.GetStringAsync(new Uri("/", UriKind.Relative));
        var match = TokenField().Match(page);
        Assert.True(match.Success);
        var token = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        var posted = await http.PostAsync(
            new Uri("/", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_handler"] = "ask",
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
                ["Question"] = question,
            }));
        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        return posted;
    }

    private static string Resource(string key)
    {
        using var _ = Cultures.Use(Cultures.English);
        return Cultures.Text()[key];
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"|type=\"hidden\" name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenField();
}
