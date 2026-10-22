using System.Net;
using System.Text.RegularExpressions;
using Sepiabox.Catalog.Search;
using Sepiabox.Search.Questions;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The question box on <c>/</c> (S-025) on the real app with a fake translator: the form's POST, the redirect to the
/// search it was understood as, the chips, and the messages when it was not.
/// </summary>
public sealed partial class QuestionPageTests
{
    private const string LisbonDecember = """{"filters": {"kinds": ["video"], "places": {"cities": ["Lisbon"]}, "years": [2019], "months": [12]}}""";

    private static readonly TimelineItem Item = new(Ids.Of(1), AssetKinds.Photo, new DateTime(2019, 12, 24), true, null);

    [Theory]
    [InlineData("videos from Lisbon in December 2019", "en")]
    [InlineData("vídeos de Lisboa em dezembro de 2019", "en")]
    [InlineData("videos from Lisbon in December 2019", "pt-BR")]
    [InlineData("vídeos de Lisboa em dezembro de 2019", "pt-BR")]
    public async Task Redirects_to_the_search_the_question_became_with_chips_in_the_ui_language(string question, string language)
    {
        var library = new FakeLibrary(Item) { SearchResults = new SearchPage([Item], false) };
        var translator = new FakeTranslator { Json = LisbonDecember };
        await using var web = Web(library, translator);
        using var http = web.CreateSignedInClient(followRedirects: false);
        http.DefaultRequestHeaders.Add("Accept-Language", language);

        using var posted = await PostQuestionAsync(http, question);

        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        var location = posted.Headers.Location!.OriginalString;
        Assert.True(SearchQueryJson.TryParse(LisbonDecember, out var expected, out _));
        Assert.Contains("q=" + SearchQueryJson.Encode(expected), location, StringComparison.Ordinal);
        Assert.Equal(question, Assert.Single(translator.Calls).Question);

        var page = await http.GetStringAsync(new Uri(new Uri("http://localhost/"), location));

        // The results are the DSL's; the chips say what it was understood as, in the page's language.
        Assert.Equal(expected, Assert.Single(library.Searches));
        var chips = Chips(page);
        Assert.Equal(
            language == "en"
                ? ["Videos", "Year: 2019", "Month: December", "City: Lisbon"]
                : ["Vídeos", "Ano: 2019", "Mês: Dezembro", "Cidade: Lisbon"],
            chips);
        // The question stays in the box, as typed.
        Assert.Contains($"value=\"{question}\"", WebUtility.HtmlDecode(page), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(QuestionOutcome.NotUnderstood, "Ask_NotUnderstood")]
    [InlineData(QuestionOutcome.Unavailable, "Ask_Unavailable")]
    [InlineData(QuestionOutcome.Failed, "Ask_Failed")]
    public async Task Shows_a_message_and_the_timeline_when_the_question_was_not_understood(QuestionOutcome outcome, string message)
    {
        var library = new FakeLibrary(Item);
        await using var web = Web(library, new FakeTranslator { Outcome = outcome });
        using var http = web.CreateSignedInClient(followRedirects: false);
        http.DefaultRequestHeaders.Add("Accept-Language", "pt-BR");

        using var posted = await PostQuestionAsync(http, "algo estranho");
        var page = WebUtility.HtmlDecode(await posted.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Contains(Resource(message, "pt-BR"), page, StringComparison.Ordinal);
        Assert.Contains("class=\"grid\"", page, StringComparison.Ordinal);
        Assert.Empty(library.Searches);
    }

    [Fact]
    public async Task Shows_a_message_when_an_answer_fails_the_dsl_parser()
    {
        await using var web = Web(new FakeLibrary(Item), new FakeTranslator { Json = """{"filters": {"faces": ["Ana"]}}""" });
        using var http = web.CreateSignedInClient(followRedirects: false);

        using var posted = await PostQuestionAsync(http, "Ana");

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Contains(Resource("Ask_NotUnderstood", "en"), WebUtility.HtmlDecode(await posted.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shows_the_daily_limit_message_once_it_is_reached()
    {
        await using var web = Web(new FakeLibrary(Item), new FakeTranslator { Json = LisbonDecember }, limit: 0);
        using var http = web.CreateSignedInClient(followRedirects: false);

        using var posted = await PostQuestionAsync(http, "videos");

        Assert.Contains(Resource("Ask_LimitReached", "en", 0), WebUtility.HtmlDecode(await posted.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pt-BR")]
    public async Task Has_a_thinking_line_in_the_form_for_the_script_to_show_and_loads_the_script(string language)
    {
        await using var web = Web(new FakeLibrary(Item) { SearchResults = new SearchPage([Item], false) }, new FakeTranslator { Json = LisbonDecember });
        using var http = web.CreateSignedInClient();
        http.DefaultRequestHeaders.Add("Accept-Language", language);

        var page = await http.GetStringAsync(new Uri("/", UriKind.Relative));

        var form = Regex.Match(page, "<form class=\"ask\".*?</form>", RegexOptions.Singleline).Value;
        Assert.Contains("class=\"ask-status\" role=\"status\"", form, StringComparison.Ordinal);
        Assert.Contains(Resource("Ask_Thinking", language), WebUtility.HtmlDecode(form), StringComparison.Ordinal);
        Assert.Contains("ask.js", page, StringComparison.Ordinal);
        using var script = await http.GetAsync(new Uri(Regex.Match(page, "src=\"(ask[^\"]*\\.js)\"").Groups[1].Value, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
    }

    [Fact]
    public async Task Says_questions_are_not_set_up_without_a_key_and_the_filters_still_work()
    {
        await using var web = new WebFactory(new FakeLibrary(Item));
        using var http = web.CreateSignedInClient();

        var page = await http.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains(Resource("Ask_NotConfigured", "en"), WebUtility.HtmlDecode(page), StringComparison.Ordinal);
        Assert.Matches("<input type=\"search\" name=\"Question\"[^>]* disabled", page);
        Assert.Contains("class=\"filters\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_a_question_posted_without_the_antiforgery_token()
    {
        var translator = new FakeTranslator { Json = LisbonDecember };
        await using var web = Web(new FakeLibrary(Item), translator);
        using var http = web.CreateSignedInClient(followRedirects: false);

        using var posted = await http.PostAsync(
            new Uri("/", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["_handler"] = "ask", ["Question"] = "videos" }));

        Assert.NotEqual(HttpStatusCode.Redirect, posted.StatusCode);
        Assert.Empty(translator.Calls);
    }

    [Fact]
    public async Task A_chip_links_to_the_same_search_without_it()
    {
        Assert.True(SearchQueryJson.TryParse(LisbonDecember, out var query, out _));
        await using var web = Web(new FakeLibrary(Item), new FakeTranslator());
        using var http = web.CreateSignedInClient();

        var page = await http.GetStringAsync(new Uri("/?q=" + SearchQueryJson.Encode(query), UriKind.Relative));

        var withoutCity = query with { Filters = query.Filters with { Places = null } };
        Assert.Contains($"href=\"{SearchLinks.Timeline(withoutCity)}\"", page, StringComparison.Ordinal);
    }

    private static WebFactory Web(FakeLibrary library, FakeTranslator translator, int limit = QuestionLimits.DefaultDailyLimit) =>
        new(library)
        {
            QuestionTranslator = translator,
            Settings = new() { ["Questions:DailyLimit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };

    // As a browser: read the form's antiforgery token from the page, then post the question with it.
    private static async Task<HttpResponseMessage> PostQuestionAsync(HttpClient http, string question)
    {
        var page = await http.GetStringAsync(new Uri("/", UriKind.Relative));
        var match = TokenField().Match(page);
        var token = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        Assert.True(match.Success, page[..Math.Min(page.Length, 4000)]);
        return await http.PostAsync(
            new Uri("/", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_handler"] = "ask",
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
                ["Question"] = question,
            }));
    }

    private static List<string> Chips(string page) =>
        [.. ChipLabel().Matches(page).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))];

    private static string Resource(string key, string culture, params object[] arguments)
    {
        using var _ = Cultures.Use(culture);
        return arguments.Length == 0 ? Cultures.Text()[key] : Cultures.Text()[key, arguments];
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"|type=\"hidden\" name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex TokenField();

    [GeneratedRegex("<li>\\s*<span>([^<]+)</span>")]
    private static partial Regex ChipLabel();
}
