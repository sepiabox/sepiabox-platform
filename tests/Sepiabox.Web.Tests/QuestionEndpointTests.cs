using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Sepiabox.Search.Questions;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary><c>POST /api/questions</c> (S-025) on the real app, with a fake translator and an in-memory library.</summary>
public sealed class QuestionEndpointTests
{
    private const string LisbonDecember = """{"filters": {"kinds": ["video"], "places": {"cities": ["Lisbon"]}, "years": [2019], "months": [12]}}""";

    private static readonly TimelineItem Found =
        new(Ids.Of(7), AssetKinds.Video, new DateTime(2019, 12, 24, 20, 0, 0), true, DurationSeconds: 3);

    [Fact]
    public async Task Answers_with_the_query_the_question_became_and_its_results()
    {
        var library = new FakeLibrary { SearchResults = new SearchPage([Found], HasMore: false) };
        var translator = new FakeTranslator { Json = LisbonDecember };
        await using var web = Web(library, translator);
        using var http = web.CreateSignedInClient();

        using var response = await AskAsync(http, "videos from Lisbon in December 2019");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var query = body.RootElement.GetProperty("query");
        Assert.Equal("Lisbon", query.GetProperty("filters").GetProperty("places").GetProperty("cities")[0].GetString());
        Assert.Equal(Ids.Of(7), Assert.Single(body.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        var searched = Assert.Single(library.Searches);
        Assert.Equal(["Lisbon"], searched.Filters.Places!.Cities!);
        Assert.Equal([12], searched.Filters.Months!);
    }

    [Theory]
    [InlineData("videos from Lisbon in December 2019", "en")]
    [InlineData("videos from Lisbon in December 2019", "pt-BR")]
    [InlineData("vídeos de Lisboa em dezembro de 2019", "en")]
    [InlineData("vídeos de Lisboa em dezembro de 2019", "pt-BR")]
    public async Task Passes_the_question_unchanged_whatever_the_ui_language(string question, string language)
    {
        var translator = new FakeTranslator { Json = LisbonDecember };
        await using var web = Web(new FakeLibrary(), translator);
        using var http = web.CreateSignedInClient();
        http.DefaultRequestHeaders.Add("Accept-Language", language);

        using var response = await AskAsync(http, question);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(question, Assert.Single(translator.Calls).Question);
    }

    [Fact]
    public async Task Gives_the_translator_today_in_the_library_s_time_zone_and_the_catalog_s_names()
    {
        var library = new FakeLibrary { Options = new([2021, 2019], ["Lisbon", "Porto"], ["HD da Ana"], ["BR", "PT"]) };
        var translator = new FakeTranslator { Json = LisbonDecember };
        // 23:30 UTC on 19 October is 00:30 on the 20th in Lisbon, still on summer time (UTC+1).
        await using var web = Web(library, translator, time: new Clock(new DateTimeOffset(2026, 10, 19, 23, 30, 0, TimeSpan.Zero)));
        using var http = web.CreateSignedInClient();

        await AskAsync(http, "fotos");

        var context = Assert.Single(translator.Calls).Context;
        Assert.Equal(new DateOnly(2026, 10, 20), context.Today);
        Assert.Equal("Europe/Lisbon", context.TimeZone);
        Assert.Equal(["HD da Ana"], context.Lists.Single(l => l.DslField == "filters.sources").Values);
        Assert.Equal(["Lisbon", "Porto"], context.Lists.Single(l => l.DslField == "filters.places.cities").Values);
        Assert.Equal(["BR", "PT"], context.Lists.Single(l => l.DslField == "filters.places.countries").Values);
    }

    [Theory]
    [InlineData(QuestionOutcome.NotUnderstood, HttpStatusCode.UnprocessableEntity, "Ask_NotUnderstood")]
    [InlineData(QuestionOutcome.Unavailable, HttpStatusCode.ServiceUnavailable, "Ask_Unavailable")]
    [InlineData(QuestionOutcome.Failed, HttpStatusCode.BadGateway, "Ask_Failed")]
    public async Task Answers_a_failed_translation_with_a_message_never_an_error_page(QuestionOutcome outcome, HttpStatusCode status, string message)
    {
        var library = new FakeLibrary();
        await using var web = Web(library, new FakeTranslator { Outcome = outcome });
        using var http = web.CreateSignedInClient();
        http.DefaultRequestHeaders.Add("Accept-Language", "pt-BR");

        using var response = await AskAsync(http, "fotos");

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(Resource(message, "pt-BR"), await DetailAsync(response));
        Assert.Empty(library.Searches);
    }

    [Theory]
    [InlineData("""{"filters": {"months": [13]}}""")]
    [InlineData("""{"filters": {"faces": ["Ana"]}}""")]
    [InlineData("""{"filters": {}, "sql": "DROP TABLE assets"}""")]
    [InlineData("not json")]
    public async Task Never_searches_with_an_answer_the_dsl_parser_rejects(string json)
    {
        var library = new FakeLibrary();
        await using var web = Web(library, new FakeTranslator { Json = json });
        using var http = web.CreateSignedInClient();

        using var response = await AskAsync(http, "fotos");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(Resource("Ask_NotUnderstood", "en"), await DetailAsync(response));
        Assert.Empty(library.Searches);
    }

    [Fact]
    public async Task Says_nothing_could_be_filtered_when_the_query_is_empty()
    {
        var library = new FakeLibrary();
        await using var web = Web(library, new FakeTranslator { Json = """{"filters": {}}""" });
        using var http = web.CreateSignedInClient();

        using var response = await AskAsync(http, "write a poem");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(Resource("Ask_NothingToFilter", "en"), await DetailAsync(response));
        Assert.Empty(library.Searches);
    }

    [Fact]
    public async Task Answers_429_once_the_daily_limit_is_reached_and_records_the_tokens()
    {
        var quota = new FakeQuestionQuota();
        var translator = new FakeTranslator { Json = LisbonDecember };
        await using var web = Web(new FakeLibrary(), translator, quota, limit: 2);
        using var http = web.CreateSignedInClient();
        http.DefaultRequestHeaders.Add("Accept-Language", "pt-BR");

        using var first = await AskAsync(http, "um");
        using var second = await AskAsync(http, "dois");
        using var third = await AskAsync(http, "três");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(Resource("Ask_LimitReached", "pt-BR", 2), await DetailAsync(third));
        Assert.Equal(2, translator.Calls.Count);
        var (key, (questions, usage)) = Assert.Single(quota.Days);
        Assert.Equal(2, questions);
        Assert.Equal(new TokenUsage(200, 40, 160, 0), usage);
        // Kept per person without keeping who: a SHA-256, not the address.
        Assert.Matches("^[0-9a-f]{64}$", key.UserKey);
        Assert.DoesNotContain("example.com", key.UserKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Counts_each_person_separately()
    {
        var quota = new FakeQuestionQuota();
        await using var web = Web(new FakeLibrary(), new FakeTranslator { Json = LisbonDecember }, quota, limit: 1, access: $"{TestUsers.Allowed},{TestUsers.Other}");
        using var ana = web.CreateSignedInClient(TestUsers.Allowed);
        using var bruno = web.CreateSignedInClient(TestUsers.Other);

        using var first = await AskAsync(ana, "one");
        using var second = await AskAsync(bruno, "two");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, quota.Days.Count);
    }

    [Fact]
    public async Task Says_questions_are_off_without_a_key_and_counts_nothing()
    {
        var quota = new FakeQuestionQuota();
        var translator = new FakeTranslator { IsConfigured = false };
        await using var web = Web(new FakeLibrary(), translator, quota);
        using var http = web.CreateSignedInClient();

        using var response = await AskAsync(http, "fotos");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(Resource("Ask_NotConfigured", "en"), await DetailAsync(response));
        Assert.Empty(translator.Calls);
        Assert.Empty(quota.Days);
    }

    [Fact]
    public async Task The_real_translator_without_a_key_turns_questions_off()
    {
        await using var web = new WebFactory(new FakeLibrary()) { Settings = new() { ["Anthropic:ApiKey"] = " " } };
        using var http = web.CreateSignedInClient();

        using var response = await AskAsync(http, "fotos");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"question": ""}""")]
    [InlineData("""{"question": "   "}""")]
    [InlineData("""{}""")]
    [InlineData("""{"question": """)]
    public async Task Refuses_an_empty_or_malformed_question(string body)
    {
        var translator = new FakeTranslator();
        await using var web = Web(new FakeLibrary(), translator);
        using var http = web.CreateSignedInClient();

        using var response = await http.PostAsync(new Uri("/api/questions", UriKind.Relative), new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(translator.Calls);
    }

    [Fact]
    public async Task Refuses_an_overlong_question()
    {
        var translator = new FakeTranslator();
        await using var web = Web(new FakeLibrary(), translator);
        using var http = web.CreateSignedInClient();

        using var response = await AskAsync(http, new string('a', QuestionPrompt.MaxQuestionLength + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(translator.Calls);
    }

    [Fact]
    public async Task Refuses_a_body_that_is_not_json()
    {
        await using var web = Web(new FakeLibrary(), new FakeTranslator());
        using var http = web.CreateSignedInClient();

        using var response = await http.PostAsync(new Uri("/api/questions", UriKind.Relative), new StringContent("question=fotos", Encoding.UTF8, "application/x-www-form-urlencoded"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Is_behind_the_access_list()
    {
        var translator = new FakeTranslator();
        await using var web = Web(new FakeLibrary(), translator);
        using var stranger = web.CreateSignedInClient(TestUsers.Stranger, followRedirects: false);

        using var response = await AskAsync(stranger, "fotos");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(translator.Calls);
    }

    private static WebFactory Web(
        FakeLibrary library, FakeTranslator translator, FakeQuestionQuota? quota = null, int? limit = null, TimeProvider? time = null, string access = TestUsers.Allowed) =>
        new(library)
        {
            AccessEmails = access,
            QuestionTranslator = translator,
            QuestionQuota = quota ?? new FakeQuestionQuota(),
            Time = time,
            Settings = new()
            {
                ["Search:TimeZone"] = "Europe/Lisbon",
                ["Questions:DailyLimit"] = (limit ?? QuestionLimits.DefaultDailyLimit).ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
        };

    private static Task<HttpResponseMessage> AskAsync(HttpClient http, string question) =>
        http.PostAsJsonAsync(new Uri("/api/questions", UriKind.Relative), new { question });

    private static async Task<string?> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static string Resource(string key, string culture, params object[] arguments)
    {
        using var _ = Cultures.Use(culture);
        return arguments.Length == 0 ? Cultures.Text()[key] : Cultures.Text()[key, arguments];
    }
}
