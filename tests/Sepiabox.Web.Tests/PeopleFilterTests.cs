using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Catalog.Search;
using Sepiabox.Search.Questions;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Components.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The people filter in the DSL, the links, the chips, the counts and the question context (S-021).</summary>
public sealed class PeopleFilterTests
{
    [Theory]
    [InlineData("""{"filters":{"people":{"all":["Ana","Pedro"],"includeMe":true}}}""", null)]
    [InlineData("""{"filters":{"people":{"includeMe":true}}}""", null)]
    [InlineData("""{"filters":{"people":{}}}""", "filters.people needs all, includeMe or both.")]
    [InlineData("""{"filters":{"people":{"includeMe":false}}}""", "filters.people needs all, includeMe or both.")]
    [InlineData("""{"filters":{"people":{"all":[]}}}""", "filters.people.all must have 1 to 50 values.")]
    [InlineData("""{"filters":{"people":{"all":[" "]}}}""", "filters.people.all: ' ' must be a name of 1 to 200 characters without control characters.")]
    [InlineData("""{"filters":{},"aggregate":{"count":"assets","groupBy":"person","top":3}}""", null)]
    public void Validates_the_people_filter_and_the_person_grouping(string json, string? expected)
    {
        var valid = SearchQueryJson.TryParse(json, out var query, out var error);

        Assert.Equal(expected is null, valid);
        if (expected is null)
        {
            Assert.True(SearchQueryJson.TryParse(SearchQueryJson.Serialize(query), out var again, out _));
            Assert.Equal(query, again);
        }
        else
        {
            Assert.Equal(expected, error);
        }
    }

    [Fact]
    public void Turns_the_filter_bar_s_people_and_me_into_a_query_and_back()
    {
        var query = SearchLinks.FromFields(null, null, null, null, null, people: ["Ana", " Pedro ", "ana", "", null], me: true);

        Assert.Equal(new PeopleFilter { All = ["Ana", "Pedro"], IncludeMe = true }, query.Filters.People);
        var fields = SearchLinks.ToFields(query);
        Assert.Equal(["Ana", "Pedro"], fields.People);
        Assert.True(fields.Me);
        Assert.Null(SearchLinks.FromFields(null, null, null, null, null).Filters.People);
    }

    [Fact]
    public void Shows_people_and_me_as_chips_each_removable()
    {
        using var _ = Cultures.Use(Cultures.BrazilianPortuguese);
        var query = new SearchQuery { Filters = new SearchFilters { People = new PeopleFilter { All = ["Ana", "Pedro"], IncludeMe = true } } };

        var chips = SearchChips.For(query, Cultures.Text());

        Assert.Equal(["Com: Ana, Pedro", "Comigo"], chips.Select(c => c.Label));
        Assert.Equal(new PeopleFilter { IncludeMe = true }, chips[0].Without.Filters.People);
        Assert.Equal(new PeopleFilter { All = ["Ana", "Pedro"] }, chips[1].Without.Filters.People);
    }

    [Fact]
    public void Links_each_person_of_a_ranking_to_their_photos_with_the_people_asked_for()
    {
        using var _ = Cultures.Use(Cultures.English);
        var query = new SearchQuery
        {
            Filters = new SearchFilters { Kinds = ["photo"], People = new PeopleFilter { IncludeMe = true } },
            Aggregate = new SearchAggregate { GroupBy = SearchGroupBy.Person, Top = 3 },
        };

        var rows = CountAnswers.Rows(query, new CountResult(6, [new CountGroup("Ana", 4)], null, 1), Cultures.Text());

        Assert.Equal("Photos by person", CountAnswers.Heading(query, Cultures.Text()));
        Assert.Equal(("Ana", 4), (rows[0].Label, rows[0].Count));
        Assert.Equal(new PeopleFilter { All = ["Ana"], IncludeMe = true }, rows[0].Items!.Filters.People);
        Assert.Null(rows[0].Items!.Aggregate);
    }

    [Theory]
    [InlineData("pt-BR", new[] { "Zé" }, false, "Não há ninguém chamado Zé.")]
    [InlineData("pt-BR", new[] { "Zé", "Bia" }, false, "Não há ninguém chamado Zé e Bia.")]
    [InlineData("pt-BR", new[] { "Zé", "Bia", "Rui" }, true, "Não há ninguém chamado Zé, Bia e Rui. Você ainda não marcou “Sou eu” em nenhuma pessoa. Encontre a sua em Pessoas e marque “Sou eu”.")]
    [InlineData("en", new[] { "Zé", "Bia" }, false, "No one is called Zé and Bia.")]
    [InlineData("en", new string[0], true, "You have not marked “This is me” on anyone yet. Find yourself on People and mark “This is me”.")]
    public void Names_the_missing_people_as_a_sentence_reads_them(string culture, string[] missing, bool me, string expected)
    {
        using var _ = Cultures.Use(culture == "en" ? Cultures.English : Cultures.BrazilianPortuguese);

        Assert.Equal(expected, PeopleSearch.Message(new UnknownPeopleException(missing, me), Cultures.Text()));
    }

    [Fact]
    public async Task Gives_the_translator_people_s_names_and_never_an_address()
    {
        var library = new FakeLibrary { Options = new FilterOptions([2025], ["Lisbon"], ["HD"]) { People = ["Ana", "Pedro"], MeLinked = true } };
        var translator = new FakeTranslator();
        var questions = new QuestionService(translator, new FakeQuestionQuota(), library, new QuestionLimits(), TimeProvider.System);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "carla@example.com")], "test"));

        await questions.AskAsync("fotos minhas com a Ana", user);

        var context = Assert.Single(translator.Calls).Context;
        Assert.Equal(["Ana", "Pedro"], context.Lists.Single(l => l.DslField == "filters.people.all").Values);
        var message = QuestionPrompt.UserMessage("fotos minhas com a Ana", context);
        Assert.Contains("\"Pedro\"", message, StringComparison.Ordinal);
        Assert.DoesNotContain("carla@example.com", message, StringComparison.Ordinal);
    }
}

/// <summary>The people selector in the filter bar and the people of a photo (bUnit).</summary>
public sealed class PeopleFilterPageTests : LocalizedContext
{
    [Fact]
    public void Offers_the_people_and_with_me_when_the_user_said_this_is_me()
    {
        var options = new FilterOptions([], [], []) { People = ["Ana", "Pedro"], MeLinked = true };
        var fields = new FilterFields(null, null, null, null, null) { People = ["Pedro"], Me = true };

        var bar = Render<FilterBar>(p => p.Add(b => b.Options, options).Add(b => b.Fields, fields));

        Assert.Equal(["Ana", "Pedro"], bar.FindAll("input[name=person]").Select(o => o.GetAttribute("value")));
        Assert.Equal(["Pedro"], bar.FindAll("input[name=person][checked]").Select(o => o.GetAttribute("value")));
        Assert.True(bar.Find("input[name=me]").HasAttribute("checked"));
    }

    [Fact]
    public void Offers_no_with_me_to_a_user_who_has_not_said_this_is_me()
    {
        var bar = Render<FilterBar>(p => p.Add(b => b.Options, new FilterOptions([], [], []) { People = ["Ana"] }));

        Assert.Single(bar.FindAll("input[name=person]"));
        Assert.Empty(bar.FindAll("input[name=me]"));
    }

    [Fact]
    public void Lists_the_people_of_a_photo_linking_named_ones_to_their_photos()
    {
        var asset = Details.Photo(Ids.Of(5), new DateTime(2019, 12, 1));
        var library = new FakeLibrary();
        library.Assets[asset.Id] = asset with { Faces = [new AssetFace(1, 3, "Ana", null), new AssetFace(2, 9, null, null)] };
        Services.AddSingleton<ILibraryQueries>(library);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/photos/{asset.Id}");

        var page = Render<PhotoDetail>(p => p.Add(d => d.Id, asset.Id));

        var links = page.FindAll(".people-card .faces a");
        Assert.Equal(["Ana", "Add a name"], links.Select(a => a.QuerySelector(".name")!.TextContent));
        var ana = SearchLinks.Timeline(new SearchQuery { Filters = new SearchFilters { People = new PeopleFilter { All = ["Ana"] } } });
        Assert.Equal([ana, "people/9"], links.Select(a => a.GetAttribute("href")));
    }
}
