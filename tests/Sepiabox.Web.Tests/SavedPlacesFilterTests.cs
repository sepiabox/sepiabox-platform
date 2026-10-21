using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Catalog.SavedPlaces;
using Sepiabox.Catalog.Search;
using Sepiabox.Search.Questions;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Components.Search;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The family's places in the DSL, the links, the chips and the question context (S-014). Made-up coordinates.</summary>
public sealed class SavedPlacesFilterTests
{
    [Theory]
    [InlineData("""{"filters":{"savedPlaces":["Casa da praia"],"distanceFrom":{"home":true,"minKm":100}}}""", null)]
    [InlineData("""{"filters":{"distanceFrom":{"place":"Casa da praia","maxKm":5}}}""", null)]
    [InlineData("""{"filters":{"distanceFrom":{"home":true,"minKm":10,"maxKm":50.5}}}""", null)]
    [InlineData("""{"filters":{"savedPlaces":[]}}""", "filters.savedPlaces must have 1 to 50 values.")]
    [InlineData("""{"filters":{"distanceFrom":{"minKm":100}}}""", "filters.distanceFrom needs exactly one of place or home.")]
    [InlineData("""{"filters":{"distanceFrom":{"place":"Casa","home":true,"minKm":100}}}""", "filters.distanceFrom needs exactly one of place or home.")]
    [InlineData("""{"filters":{"distanceFrom":{"home":false,"minKm":100}}}""", "filters.distanceFrom needs exactly one of place or home.")]
    [InlineData("""{"filters":{"distanceFrom":{"home":true}}}""", "filters.distanceFrom needs minKm, maxKm or both.")]
    [InlineData("""{"filters":{"distanceFrom":{"home":true,"minKm":100,"maxKm":50}}}""", "filters.distanceFrom.minKm must be less than maxKm.")]
    [InlineData("""{"filters":{"distanceFrom":{"home":true,"minKm":-1}}}""", "filters.distanceFrom.minKm and maxKm must be between 0 and 20040 (maxKm above 0).")]
    [InlineData("""{"filters":{"distanceFrom":{"home":true,"maxKm":0}}}""", "filters.distanceFrom.minKm and maxKm must be between 0 and 20040 (maxKm above 0).")]
    [InlineData("""{"filters":{"distanceFrom":{"place":" ","maxKm":1}}}""", "filters.distanceFrom.place: ' ' must be a name of 1 to 200 characters without control characters.")]
    public void Validates_saved_places_and_the_distance(string json, string? expected)
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
    public void Turns_the_filter_bar_s_places_into_a_query_and_back()
    {
        var query = SearchLinks.FromFields(null, null, null, null, null, savedPlaces: ["Casa da praia", " casa da praia ", "Casa", ""]);

        Assert.Equal(["Casa da praia", "Casa"], query.Filters.SavedPlaces);
        Assert.Equal(["Casa da praia", "Casa"], SearchLinks.ToFields(query).SavedPlaces);
        Assert.Null(SearchLinks.FromFields(null, null, null, null, null).Filters.SavedPlaces);
    }

    [Theory]
    [InlineData(100.0, null, "A mais de 100 km de casa")]
    [InlineData(null, 2.5, "Até 2,5 km de casa")]
    [InlineData(10.0, 50.0, "Entre 10 e 50 km de casa")]
    public void Shows_places_and_the_distance_from_home_as_removable_chips_in_brazilian_portuguese(double? min, double? max, string expected)
    {
        using var _ = Cultures.Use(Cultures.BrazilianPortuguese);
        var query = new SearchQuery
        {
            Filters = new SearchFilters { SavedPlaces = ["Casa da praia"], DistanceFrom = new DistanceFilter { Home = true, MinKm = min, MaxKm = max } },
        };

        var chips = SearchChips.For(query, Cultures.Text());

        Assert.Equal(["Em: Casa da praia", expected], chips.Select(c => c.Label));
        Assert.Null(chips[0].Without.Filters.SavedPlaces);
        Assert.Null(chips[1].Without.Filters.DistanceFrom);
        Assert.NotNull(chips[1].Without.Filters.SavedPlaces);
    }

    [Fact]
    public void Names_the_place_in_a_distance_chip_in_english()
    {
        using var _ = Cultures.Use(Cultures.English);
        var query = new SearchQuery { Filters = new SearchFilters { DistanceFrom = new DistanceFilter { Place = "Casa da praia", MinKm = 100 } } };

        Assert.Equal("More than 100 km from Casa da praia", Assert.Single(SearchChips.For(query, Cultures.Text())).Label);
    }

    [Theory]
    [InlineData("38.72, -9.13", 38.72, -9.13)]
    [InlineData(" 38.72,-9.13 ", 38.72, -9.13)]
    [InlineData("38.72; -9.13", 38.72, -9.13)]
    [InlineData("38,72 -9,13", 38.72, -9.13)]
    [InlineData("38,72; -9,13", 38.72, -9.13)]
    [InlineData("-22.9068, -43.1729", -22.9068, -43.1729)]
    public void Reads_coordinates_typed_or_pasted_from_a_map(string text, double latitude, double longitude)
    {
        Assert.Equal((latitude, longitude), PlaceCoordinates.TryParse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("38.72")]
    [InlineData("north, west")]
    [InlineData("91, 0")]
    [InlineData("0, 181")]
    [InlineData("38.72, -9.13, 4")]
    public void Refuses_what_is_not_a_latitude_and_a_longitude(string? text)
    {
        Assert.Null(PlaceCoordinates.TryParse(text));
    }

    [Fact]
    public async Task Gives_the_translator_the_places_names_and_the_home_never_where_they_are()
    {
        var library = new FakeLibrary
        {
            Options = new FilterOptions([2025], ["Lisbon"], ["HD"]) { SavedPlaces = ["Casa", "Casa da praia"], HomePlace = "Casa" },
        };
        var translator = new FakeTranslator();
        var questions = new QuestionService(translator, new FakeQuestionQuota(), library, new QuestionLimits(), TimeProvider.System);

        await questions.AskAsync("fotos a mais de 100 km de casa", new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "ana@example.com")], "test")));

        var context = Assert.Single(translator.Calls).Context;
        Assert.Equal(["Casa", "Casa da praia"], context.Lists.Single(l => l.Name == "saved places").Values);
        Assert.Equal(["Casa"], context.Lists.Single(l => l.Name == "home place").Values);
        Assert.All(context.Lists.Where(l => l.Name.Contains("place", StringComparison.Ordinal)), l => Assert.Equal("filters.savedPlaces", l.DslField));
        Assert.Contains("\"Casa da praia\"", QuestionPrompt.UserMessage("x", context), StringComparison.Ordinal);
    }
}

/// <summary>The places page, the photo's "Save this place" and the filter bar's places (bUnit).</summary>
public sealed class SavedPlacesPageTests : LocalizedContext
{
    private readonly FakeSavedPlaces _places = new();

    public SavedPlacesPageTests()
    {
        Services.AddSingleton<ISavedPlaces>(_places);
        Services.AddSingleton<ILibraryQueries>(new FakeLibrary());
        _places.Places.Add(new SavedPlaceSummary(1, "Casa", 38.74, -9.16, 300, IsHome: true));
        _places.Places.Add(new SavedPlaceSummary(2, "Casa da praia", 38.96, -9.41, 500, IsHome: false));
    }

    [Fact]
    public void Lists_the_places_with_the_home_marked_and_links_to_their_photos_and_years()
    {
        var page = Render<Places>();

        Assert.Equal(["Casa (home)", "Casa da praia"], page.FindAll(".saved-place h2").Select(h => Collapse(h.TextContent)));
        Assert.Equal("38.74, -9.16 · radius 300 m", page.Find(".saved-place .coordinates").TextContent);
        var years = SearchLinks.Timeline(new SearchQuery
        {
            Filters = new SearchFilters { SavedPlaces = ["Casa da praia"] },
            Aggregate = new SearchAggregate { GroupBy = SearchGroupBy.Year },
        });
        Assert.Equal(years, page.FindAll(".over-the-years")[1].GetAttribute("href"));
        Assert.Equal(["Casa", "Casa da praia"], page.FindAll("form.edit input[name=EditName]").Select(i => i.GetAttribute("value")));
        Assert.True(page.FindAll("form.edit input[name=EditHome]")[0].HasAttribute("checked"));
        Assert.Empty(page.FindAll("form.confirm"));
    }

    [Fact]
    public void Fills_in_a_photo_s_coordinates_and_asks_before_deleting_in_brazilian_portuguese()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        var library = new FakeLibrary();
        library.Assets[Ids.Of(5)] = Details.Photo(Ids.Of(5), new DateTime(2019, 12, 1)) with { Location = (38.96031234, -9.41) };
        Services.AddSingleton<ILibraryQueries>(library);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"places?asset={Ids.Of(5)}&delete=2");

        var page = Render<Places>();

        Assert.Equal("38.960312, -9.41", page.Find("input[name=NewCoordinates]").GetAttribute("value"));
        Assert.Equal("200", page.Find("input[name=NewRadius]").GetAttribute("value"));
        Assert.Equal("60", page.Find("input[name=NewName]").GetAttribute("maxlength"));
        Assert.All(page.FindAll(".name-hint"), hint => Assert.Equal("O nome vai para o Claude ao responder perguntas; não use o endereço.", hint.TextContent));
        Assert.Equal(3, page.FindAll(".name-hint").Count);
        Assert.StartsWith("Um lugar não pode ser movido", page.Find(".cannot-move").TextContent, StringComparison.Ordinal);
        Assert.Equal("Apagar Casa da praia? As fotos e os vídeos tirados lá ficam; só o lugar é apagado.", page.Find(".confirm-delete .note").TextContent);
        Assert.Equal("2", page.Find("form.confirm input[name=DeleteId]").GetAttribute("value"));
    }

    [Fact]
    public void Offers_save_this_place_on_a_photo_with_a_location()
    {
        using var language = Cultures.Use(Cultures.BrazilianPortuguese);
        var library = new FakeLibrary();
        var asset = Details.Photo(Ids.Of(5), new DateTime(2019, 12, 1));
        library.Assets[asset.Id] = asset with { Location = (38.96031234, -9.41) };
        library.Assets[Ids.Of(6)] = Details.Photo(Ids.Of(6), new DateTime(2019, 12, 1));
        Services.AddSingleton<ILibraryQueries>(library);

        var withLocation = Render<PhotoDetail>(p => p.Add(d => d.Id, asset.Id));
        var without = Render<PhotoDetail>(p => p.Add(d => d.Id, Ids.Of(6)));

        // By the photo's id: its coordinates never go in an address.
        var link = withLocation.Find(".save-place a");
        Assert.Equal(("Salvar este lugar", $"places?asset={asset.Id}"), (link.TextContent, link.GetAttribute("href")));
        Assert.Empty(without.FindAll(".save-place"));
    }

    [Fact]
    public void Offers_the_saved_places_in_the_filter_bar()
    {
        var options = new FilterOptions([], [], []) { SavedPlaces = ["Casa", "Casa da praia"] };
        var fields = new FilterFields(null, null, null, null, null) { SavedPlaces = ["Casa da praia"] };

        var bar = Render<FilterBar>(p => p.Add(b => b.Options, options).Add(b => b.Fields, fields));

        Assert.Equal(["Casa", "Casa da praia"], bar.FindAll("select[name=place] option").Select(o => o.GetAttribute("value")));
        Assert.Equal(["Casa da praia"], bar.FindAll("select[name=place] option[selected]").Select(o => o.GetAttribute("value")));
        Assert.Empty(Render<FilterBar>(p => p.Add(b => b.Options, new FilterOptions([], [], []))).FindAll("select[name=place]"));
    }

    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>The places page's forms over HTTP, signed in, with an in-memory library and a recording editor.</summary>
public sealed class SavedPlacesPostTests
{
    private const string Ana = "ana@example.com";

    [Fact]
    public async Task Saves_a_place_typed_with_decimal_commas_as_the_home()
    {
        var (web, places) = Web();
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var response = await FaceSettingsPostTests.PostAsync(http, "/places", "create", new()
        {
            ["NewName"] = "Casa",
            ["NewCoordinates"] = "38,74 -9,16",
            ["NewRadius"] = "300",
            ["NewHome"] = "1",
        });
        using var withoutRadius = await FaceSettingsPostTests.PostAsync(http, "/places", "create", new()
        {
            ["NewName"] = "Casa da praia",
            ["NewCoordinates"] = "38.96, -9.41",
            ["NewRadius"] = " ",
        });

        Assert.Equal("/places?note=created", response.Headers.Location?.PathAndQuery);
        Assert.Equal(["create Casa 38.74 -9.16 300 True", "create Casa da praia 38.96 -9.41 200 False"], places.Calls);
    }

    [Theory]
    [InlineData("north, west", "200", "Write the coordinates as latitude, longitude")]
    [InlineData("38.74, -9.16", "lots", "The radius must be between 10 and 50000 metres.")]
    public async Task Shows_what_was_typed_again_with_the_reason_when_it_cannot_be_saved(string coordinates, string radius, string reason)
    {
        var (web, places) = Web();
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var response = await FaceSettingsPostTests.PostAsync(http, "/places", "create", new()
        {
            ["NewName"] = "Casa nova",
            ["NewCoordinates"] = coordinates,
            ["NewRadius"] = radius,
            ["NewHome"] = "1",
        });
        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(places.Calls);
        Assert.Contains(reason, page, StringComparison.Ordinal);
        Assert.Contains("name=\"NewName\" value=\"Casa nova\"", page, StringComparison.Ordinal);
        Assert.Contains($"name=\"NewCoordinates\" value=\"{coordinates}\"", page, StringComparison.Ordinal);
        Assert.Contains($"name=\"NewRadius\" min=\"10\" max=\"50000\" value=\"{radius}\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changes_one_place_from_its_own_form_and_says_when_the_name_is_taken()
    {
        var (web, places) = Web(SavedPlaceOutcome.NameTaken);
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var response = await FaceSettingsPostTests.PostAsync(http, "/places", "edit-2", new()
        {
            ["EditName"] = "casa",
            ["EditRadius"] = "800",
            ["EditHome"] = "1",
        });
        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["update 2 casa 800 True"], places.Calls);
        Assert.Contains("Another place already has this name.", page, StringComparison.Ordinal);
        // The place's form shows what was typed; the other one its own values.
        Assert.Contains("name=\"EditName\" value=\"casa\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"EditName\" value=\"Casa\"", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "update 2 Casa da praia keep False")]
    [InlineData("750", "update 2 Casa da praia 750 False")]
    public async Task Keeps_a_place_s_own_radius_when_the_field_is_left_blank(string radius, string call)
    {
        var (web, places) = Web();
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var response = await FaceSettingsPostTests.PostAsync(http, "/places", "edit-2", new()
        {
            ["EditName"] = "Casa da praia",
            ["EditRadius"] = radius,
        });

        Assert.Equal("/places?note=updated", response.Headers.Location?.PathAndQuery);
        Assert.Equal([call], places.Calls);
    }

    [Fact]
    public async Task Deletes_a_place_only_from_its_confirmation()
    {
        var (web, places) = Web();
        await using var _ = web;
        using var http = web.CreateSignedInClient(Ana, followRedirects: false);

        using var response = await FaceSettingsPostTests.PostAsync(http, "/places?delete=2", "delete", new() { ["DeleteId"] = "2" });

        Assert.Equal("/places?note=deleted", response.Headers.Location?.PathAndQuery);
        Assert.Equal(["delete 2"], places.Calls);
    }

    [Fact]
    public async Task Links_places_from_the_header()
    {
        var (web, _) = Web();
        await using var __ = web;
        using var signedIn = web.CreateSignedInClient(Ana);

        var page = await signedIn.GetStringAsync(new Uri("/places", UriKind.Relative));

        Assert.Contains("href=\"places\" class=\"nav-link\"", page, StringComparison.Ordinal);
    }

    private static (WebFactory Web, FakeSavedPlaces Places) Web(SavedPlaceOutcome outcome = SavedPlaceOutcome.Done)
    {
        var places = new FakeSavedPlaces { Outcome = outcome };
        places.Places.Add(new SavedPlaceSummary(1, "Casa", 38.74, -9.16, 300, IsHome: true));
        places.Places.Add(new SavedPlaceSummary(2, "Casa da praia", 38.96, -9.41, 500, IsHome: false));
        return (new WebFactory(new FakeLibrary()) { AccessEmails = Ana, SavedPlaces = places }, places);
    }
}
