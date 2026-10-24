using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Catalog.Events;
using Sepiabox.Catalog.Search;
using Sepiabox.Search.Questions;
using Sepiabox.Web.Components.Layout;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Localization;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The title of an occasion, in the reader's language (S-023, D-050).</summary>
public sealed class OccasionTitleTests
{
    [Theory]
    [InlineData(Cultures.English, "Lisbon", "2024-12-14", "2024-12-16", "Lisbon · Dec 14–16, 2024")]
    [InlineData(Cultures.BrazilianPortuguese, "Lisbon", "2024-12-14", "2024-12-16", "Lisbon · 14–16 dez 2024")]
    [InlineData(Cultures.English, "Porto", "2024-12-14", "2024-12-14", "Porto · Dec 14, 2024")]
    [InlineData(Cultures.BrazilianPortuguese, "Porto", "2024-12-14", "2024-12-14", "Porto · 14 dez 2024")]
    [InlineData(Cultures.English, "Faro", "2024-11-28", "2024-12-02", "Faro · Nov 28 – Dec 2, 2024")]
    [InlineData(Cultures.BrazilianPortuguese, "Faro", "2024-11-28", "2024-12-02", "Faro · 28 nov – 2 dez 2024")]
    [InlineData(Cultures.English, "Sintra", "2024-12-30", "2025-01-02", "Sintra · Dec 30, 2024 – Jan 2, 2025")]
    [InlineData(Cultures.BrazilianPortuguese, "Sintra", "2024-12-30", "2025-01-02", "Sintra · 30 dez 2024 – 2 jan 2025")]
    [InlineData(Cultures.BrazilianPortuguese, "Natal", "2024-05-03", "2024-05-05", "Natal · 3–5 mai 2024")]
    [InlineData(Cultures.English, null, "2024-12-14", "2024-12-16", "Dec 14–16, 2024")]
    [InlineData(Cultures.BrazilianPortuguese, " ", "2024-12-14", "2024-12-16", "14–16 dez 2024")]
    public void Writes_the_place_and_the_date_range_in_the_current_culture(string culture, string? place, string first, string last, string expected)
    {
        using var _ = Cultures.Use(culture);

        var title = Cultures.Text().OccasionTitle(place, Day(first, 9, 30), Day(last, 22, 5));

        Assert.Equal(expected, title);
    }

    [Fact]
    public void A_title_is_computed_from_the_dates_so_it_follows_the_language_it_is_read_in()
    {
        var (start, end) = (Day("2024-12-14", 10, 0), Day("2024-12-16", 18, 0));

        string English()
        {
            using var _ = Cultures.Use(Cultures.English);
            return Cultures.Text().OccasionTitle("Lisbon", start, end);
        }

        string Portuguese()
        {
            using var _ = Cultures.Use(Cultures.BrazilianPortuguese);
            return Cultures.Text().OccasionTitle("Lisbon", start, end);
        }

        Assert.NotEqual(English(), Portuguese());
    }

    private static DateTime Day(string iso, int hour, int minute) => DateTime.Parse(iso, System.Globalization.CultureInfo.InvariantCulture).AddHours(hour).AddMinutes(minute);
}

/// <summary>The occasions pages (S-023): the cards, the paging, the empty state and one occasion's photos.</summary>
public sealed class OccasionsPageTests : LocalizedContext
{
    private readonly FakeLibrary _library = new();

    public OccasionsPageTests() => Services.AddSingleton<ILibraryQueries>(_library);

    [Fact]
    public void Shows_a_card_for_each_occasion_with_three_thumbnails_the_title_and_the_count()
    {
        _library.Occasions.Add(new OccasionSummary(7, new DateTime(2024, 12, 14, 9, 0, 0), new DateTime(2024, 12, 16, 20, 0, 0), "Lisbon", 120, [Ids.Of(1), Ids.Of(2), Ids.Of(3)]));
        _library.Occasions.Add(new OccasionSummary(3, new DateTime(2024, 6, 1, 9, 0, 0), new DateTime(2024, 6, 1, 9, 0, 0), null, 1, []));

        var page = RenderPage();

        var cards = page.FindAll("li.occasion");
        Assert.Equal(2, cards.Count);
        Assert.Equal("occasions/7", cards[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal(
            [$"photos/{Ids.Of(1)}/thumb-256", $"photos/{Ids.Of(2)}/thumb-256", $"photos/{Ids.Of(3)}/thumb-256"],
            cards[0].QuerySelectorAll("img").Select(i => i.GetAttribute("src")));
        Assert.Equal("Lisbon · Dec 14–16, 2024", cards[0].QuerySelector(".occasion-title")!.TextContent);
        Assert.Equal("120 photos and videos", cards[0].QuerySelector(".count")!.TextContent);
        Assert.Equal("Jun 1, 2024", cards[1].QuerySelector(".occasion-title")!.TextContent);
        Assert.Equal("1 photo or video", cards[1].QuerySelector(".count")!.TextContent);
        Assert.Empty(cards[1].QuerySelectorAll("img"));
    }

    [Fact]
    public void Writes_the_title_and_the_count_in_brazilian_portuguese()
    {
        using var _ = Cultures.Use(Cultures.BrazilianPortuguese);
        _library.Occasions.Add(new OccasionSummary(7, new DateTime(2024, 12, 14, 9, 0, 0), new DateTime(2024, 12, 16, 20, 0, 0), "Lisbon", 1200, []));

        var page = RenderPage();

        Assert.Equal("Lisbon · 14–16 dez 2024", page.Find(".occasion-title").TextContent);
        Assert.Equal("1.200 itens", page.Find(".count").TextContent);
        Assert.Equal("Ocasiões", page.Find("h1").TextContent);
    }

    [Fact]
    public void Says_there_are_no_occasions_and_how_they_are_made_when_there_are_none()
    {
        var page = RenderPage();

        Assert.Equal("There are no occasions yet.", page.Find("p.empty").TextContent);
        Assert.Equal("sepiabox-worker events rebuild", page.Find("p.muted code").TextContent);
        Assert.Equal(
            "Photos taken close together in time and place are grouped into occasions when the library owner runs sepiabox-worker events rebuild.",
            page.Find("p.muted").TextContent);
    }

    [Fact]
    public void Pages_through_the_occasions_newest_first_with_links_that_keep_the_page()
    {
        var library = new FakeLibrary { OccasionPageSize = 1 };
        foreach (var n in new[] { 3, 2, 1 })
        {
            library.Occasions.Add(new OccasionSummary(n, new DateTime(2024, n, 1), new DateTime(2024, n, 1), null, 1, []));
        }

        Services.AddSingleton<ILibraryQueries>(library);

        Assert.Equal(["occasions?page=2"], RenderPage().FindAll("nav.pages a").Select(a => a.GetAttribute("href")));
        Assert.Equal(["occasions", "occasions?page=3"], RenderPage(2).FindAll("nav.pages a").Select(a => a.GetAttribute("href")));
        Assert.Equal(["occasions?page=2"], RenderPage(3).FindAll("nav.pages a").Select(a => a.GetAttribute("href")));
        Assert.Equal(3, library.RequestedOccasionsPage);
    }

    [Fact]
    public void Shows_the_type_on_a_card_and_on_the_occasion_s_page_in_the_culture()
    {
        using var _ = Cultures.Use(Cultures.BrazilianPortuguese);
        _library.Occasions.Add(new OccasionSummary(7, new DateTime(2024, 12, 14), new DateTime(2024, 12, 14), null, 3, []) { Type = "wedding" });
        _library.Occasions.Add(new OccasionSummary(8, new DateTime(2024, 11, 1), new DateTime(2024, 11, 1), null, 1, []));

        var cards = RenderPage().FindAll("li.occasion .count");

        Assert.Equal(["3 itens", "1 item"], cards.Select(c => c.TextContent));
        // The type is a badge of its own on the card that has one.
        Assert.Equal(["Casamento"], RenderPage().FindAll("li.occasion .occasion-type").Select(b => b.TextContent));
        Assert.Contains("Casamento · 3 itens", Render<Occasion>(p => p.Add(o => o.Id, 7)).Find(".page-sub").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Heads_each_year_with_how_many_occasions_it_has_in_all_and_puts_each_year_s_cards_under_it()
    {
        var library = new FakeLibrary();
        library.Occasions.Add(new OccasionSummary(1, new DateTime(2024, 12, 14), new DateTime(2024, 12, 14), null, 3, []));
        library.Occasions.Add(new OccasionSummary(2, new DateTime(2023, 6, 1), new DateTime(2023, 6, 1), null, 1, []));
        library.OccasionYearCounts = new Dictionary<int, int> { [2024] = 1, [2023] = 40 };
        Services.AddSingleton<ILibraryQueries>(library);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/occasions");

        var page = Render<Occasions>();

        Assert.Equal(["2024", "2023"], page.FindAll(".section-title h2").Select(h => h.TextContent));
        Assert.Equal(["1", "40"], page.FindAll(".section-title .n").Select(n => n.TextContent));
        Assert.Equal(2, page.FindAll("ul.occasions").Count);
    }

    [Fact]
    public void Offers_the_types_the_occasions_have_and_lists_the_ones_chosen()
    {
        var library = new FakeLibrary
        {
            Options = new FilterOptions([], [], []) { EventTypes = ["wedding", "trip"], EventTypeCounts = new Dictionary<string, int> { ["wedding"] = 12, ["trip"] = 1200 } },
        };
        library.Occasions.Add(new OccasionSummary(7, new DateTime(2024, 12, 14), new DateTime(2024, 12, 14), null, 3, []) { Type = "wedding" });
        library.Occasions.Add(new OccasionSummary(8, new DateTime(2024, 11, 1), new DateTime(2024, 11, 1), null, 1, []) { Type = "trip" });
        Services.AddSingleton<ILibraryQueries>(library);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/occasions?type=trip");

        var page = Render<Occasions>();

        // The timeline's chip control (S-067) with a count for each type; the chosen one is checked and tints the chip.
        Assert.Equal(["", "wedding", "trip"], page.FindAll("form.occasion-filter input[name=type]").Select(o => o.GetAttribute("value")));
        Assert.Equal(["trip"], page.FindAll("form.occasion-filter input[name=type][checked]").Select(o => o.GetAttribute("value")));
        Assert.Equal(["Any", "Wedding (12)", "Trip (1,200)"], page.FindAll("form.occasion-filter label.opt").Select(o => o.TextContent.Trim()));
        Assert.True(page.Find("form.occasion-filter details.fchip").HasAttribute("data-set"));
        Assert.Equal("Trip", page.Find("form.occasion-filter summary .v").TextContent);
        Assert.Equal("occasions", page.Find("form.occasion-filter a.clear").GetAttribute("href"));
        Assert.Equal("trip", library.RequestedOccasionType);
        Assert.Single(page.FindAll("li.occasion"));
    }

    [Fact]
    public void Ignores_a_type_that_is_not_one_and_says_when_none_of_a_type_exist()
    {
        var library = new FakeLibrary { Options = new FilterOptions([], [], []) { EventTypes = ["wedding"] } };
        library.Occasions.Add(new OccasionSummary(7, new DateTime(2024, 12, 14), new DateTime(2024, 12, 14), null, 3, []) { Type = "wedding" });
        Services.AddSingleton<ILibraryQueries>(library);
        var navigation = Services.GetRequiredService<NavigationManager>();

        navigation.NavigateTo("/occasions?type=funeral");
        Assert.Single(Render<Occasions>().FindAll("li.occasion"));
        Assert.Null(library.RequestedOccasionType);

        navigation.NavigateTo("/occasions?type=party");
        var none = Render<Occasions>();
        Assert.Equal("There are no occasions of this type. Show all occasions", none.Find("p.empty").TextContent);
    }

    [Fact]
    public void Keeps_the_type_in_the_paging_links()
    {
        var library = new FakeLibrary { OccasionPageSize = 1, Options = new FilterOptions([], [], []) { EventTypes = ["wedding"] } };
        foreach (var n in new[] { 3, 2 })
        {
            library.Occasions.Add(new OccasionSummary(n, new DateTime(2024, n, 1), new DateTime(2024, n, 1), null, 1, []) { Type = "wedding" });
        }

        Services.AddSingleton<ILibraryQueries>(library);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/occasions?type=wedding");

        Assert.Equal(["occasions?type=wedding&page=2"], Render<Occasions>().FindAll("nav.pages a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void Offers_the_type_in_the_address_even_when_no_occasion_has_it()
    {
        var library = new FakeLibrary { Options = new FilterOptions([], [], []) { EventTypes = ["wedding"] } };
        Services.AddSingleton<ILibraryQueries>(library);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/occasions?type=party");

        var page = Render<Occasions>();

        Assert.Equal(["", "wedding", "party"], page.FindAll("form.occasion-filter input[name=type]").Select(o => o.GetAttribute("value")));
        Assert.Equal(["party"], page.FindAll("form.occasion-filter input[name=type][checked]").Select(o => o.GetAttribute("value")));
        Assert.Contains("Party (0)", page.FindAll("form.occasion-filter label.opt").Select(o => o.TextContent.Trim()));
    }

    [Fact]
    public void A_page_that_is_not_a_number_is_the_first()
    {
        _library.Occasions.Add(new OccasionSummary(1, new DateTime(2024, 1, 1), new DateTime(2024, 1, 1), null, 1, []));

        Services.GetRequiredService<NavigationManager>().NavigateTo("/occasions?page=abc");
        var page = Render<Occasions>();

        Assert.Equal(1, _library.RequestedOccasionsPage);
        Assert.Single(page.FindAll("li.occasion"));
    }

    [Fact]
    public void An_occasion_lists_its_photos_oldest_first_and_links_to_the_timeline_filtered_by_it()
    {
        var library = new FakeLibrary
        {
            SearchResults = new SearchPage(
            [
                new TimelineItem(Ids.Of(1), AssetKinds.Photo, new DateTime(2024, 12, 14, 9, 0, 0), true, null),
                new TimelineItem(Ids.Of(2), AssetKinds.Video, new DateTime(2024, 12, 16, 20, 0, 0), true, 3),
            ], false),
        };
        library.Occasions.Add(new OccasionSummary(7, new DateTime(2024, 12, 14, 9, 0, 0), new DateTime(2024, 12, 16, 20, 0, 0), "Lisbon", 2, []));
        Services.AddSingleton<ILibraryQueries>(library);

        var page = Render<Occasion>(p => p.Add(o => o.Id, 7));

        Assert.Equal("Lisbon · Dec 14–16, 2024", page.Find("h1").TextContent);
        Assert.Equal(2, page.FindAll("ul.grid li").Count);
        var asked = Assert.Single(library.Searches);
        Assert.Equal([7L], asked.Filters.EventIds);
        Assert.Equal((SearchSort.CapturedAtAsc, SearchQuery.MaxLimit), (asked.Sort, asked.Limit));
        var timeline = new SearchQuery { Filters = new SearchFilters { EventIds = [7] }, Sort = SearchSort.CapturedAtAsc };
        Assert.Equal(SearchLinks.Timeline(timeline), page.Find(".page-sub a").GetAttribute("href"));
        Assert.Equal("All occasions", page.Find("p.back a").TextContent);
    }

    [Fact]
    public void An_occasion_that_does_not_exist_says_so()
    {
        var page = Render<Occasion>(p => p.Add(o => o.Id, 99));

        Assert.Equal("Occasion not found", page.Find("h1").TextContent);
        Assert.Equal("It may have been replaced when the photos were regrouped.", page.Find("p.muted").FirstChild!.TextContent.Trim());
        Assert.Equal("occasions", page.Find("p.muted a").GetAttribute("href"));
        Assert.Equal("See all occasions", page.Find("p.muted a").TextContent);
        Assert.Empty(_library.Searches);
    }

    [Fact]
    public void An_occasion_with_no_photos_or_videos_says_so()
    {
        _library.Occasions.Add(new OccasionSummary(7, new DateTime(2024, 12, 14), new DateTime(2024, 12, 14), null, 0, []));

        var page = Render<Occasion>(p => p.Add(o => o.Id, 7));

        Assert.Equal("This occasion has no photos or videos.", page.Find("p.empty").TextContent);
        Assert.Empty(page.FindAll("ul.grid"));
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void The_link_to_the_timeline_is_offered_only_when_the_timeline_shows_all_of_them(int count, bool offered)
    {
        var library = new FakeLibrary { SearchResults = new SearchPage([], false) };
        library.Occasions.Add(new OccasionSummary(7, new DateTime(2024, 12, 14), new DateTime(2024, 12, 14), null, count, []));
        Services.AddSingleton<ILibraryQueries>(library);

        var page = Render<Occasion>(p => p.Add(o => o.Id, 7));

        Assert.Equal(offered, page.FindAll(".page-sub a").Count == 1);
    }

    [Fact]
    public void A_card_with_no_thumbnail_shows_a_placeholder_and_a_page_past_the_end_says_no_more_occasions()
    {
        _library.Occasions.Add(new OccasionSummary(1, new DateTime(2024, 1, 1), new DateTime(2024, 1, 1), null, 1, []));
        Assert.Equal("No thumbnails", RenderPage().Find(".occasion-thumbs .placeholder").TextContent);

        Assert.Equal("No more occasions. Back to the start", RenderPage(5).Find("p.empty").TextContent.Replace("  ", " "));
    }

    [Theory]
    [InlineData(Cultures.English, "Occasions")]
    [InlineData(Cultures.BrazilianPortuguese, "Ocasiões")]
    public void The_layout_links_to_the_occasions(string culture, string text)
    {
        using var _ = Cultures.Use(culture);
        AddAuthorization().SetAuthorized("ana@example.com");
        Services.AddSingleton<AntiforgeryStateProvider>(new FixedAntiforgery());

        var layout = Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "page"))));

        Assert.Contains(layout.FindAll("header a.nav-link"), a => a.GetAttribute("href") == "occasions" && a.TextContent == text);
    }

    private sealed class FixedAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }

    private IRenderedComponent<Occasions> RenderPage(int? page = null)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(page is null ? "/occasions" : $"/occasions?page={page}");
        return Render<Occasions>();
    }
}

/// <summary>The DSL filter <c>eventIds</c> in the chips, the filter bar's links and the question translator (S-023, D-092).</summary>
public sealed class OccasionFilterTests
{
    [Fact]
    public void Parses_event_ids_and_keeps_them_through_the_query_string_form()
    {
        Assert.True(SearchQueryJson.TryParse("""{"filters": {"eventIds": [3, 9], "kinds": ["photo"]}}""", out var query, out var error), error);

        Assert.Equal([3L, 9L], query.Filters.EventIds);
        Assert.True(SearchQueryJson.TryDecode(SearchQueryJson.Encode(query), out var decoded, out _));
        Assert.Equal(query, decoded);
        Assert.NotEqual(query, query with { Filters = query.Filters with { EventIds = [3] } });
    }

    [Theory]
    [InlineData("""{"filters": {"eventIds": []}}""", "filters.eventIds")]
    [InlineData("""{"filters": {"eventIds": [0]}}""", "filters.eventIds")]
    [InlineData("""{"filters": {"eventIds": [-1]}}""", "filters.eventIds")]
    public void Refuses_event_ids_that_are_not_occasion_ids(string json, string field)
    {
        Assert.False(SearchQueryJson.TryParse(json, out _, out var error));
        Assert.Contains(field, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_more_than_50_event_ids()
    {
        var ids = string.Join(",", Enumerable.Range(1, 51));

        Assert.False(SearchQueryJson.TryParse("{\"filters\": {\"eventIds\": [" + ids + "]}}", out _, out _));
    }

    [Fact]
    public void Refuses_a_translated_answer_with_event_ids_since_a_question_cannot_name_an_occasion()
    {
        const string Json = """{"filters": {"eventIds": [1]}}""";

        Assert.True(SearchQueryJson.TryParse(Json, out _, out _));
        Assert.Contains("eventIds", QuestionService.Validate(Json), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Cultures.English, "In an occasion", "In 2 occasions")]
    [InlineData(Cultures.BrazilianPortuguese, "Em uma ocasião", "Em 2 ocasiões")]
    public void Shows_how_many_occasions_the_search_is_inside_with_a_chip_that_removes_them(string culture, string one, string two)
    {
        using var _ = Cultures.Use(culture);
        var single = new SearchQuery { Filters = new SearchFilters { EventIds = [4], Years = [2024] } };
        var several = new SearchQuery { Filters = new SearchFilters { EventIds = [4, 5] } };

        var chips = SearchChips.For(single, Cultures.Text());

        Assert.Equal(one, chips.First(c => c.Label == one).Label);
        Assert.Equal(new SearchQuery { Filters = new SearchFilters { Years = [2024] } }, chips.Single(c => c.Label == one).Without);
        Assert.Equal(two, Assert.Single(SearchChips.For(several, Cultures.Text())).Label);
    }

    [Fact]
    public void The_filter_bar_keeps_the_occasions_while_it_narrows_them_and_ignores_ids_that_are_not_occasions()
    {
        var narrowed = SearchLinks.FromFields("2024", null, null, null, null, eventIds: ["7", "7", "x", "0", "-2", "9"]);

        Assert.Equal([7L, 9L], narrowed.Filters.EventIds);
        Assert.Equal([2024], narrowed.Filters.Years);
        Assert.Equal([7L, 9L], SearchLinks.ToFields(narrowed).EventIds);
        Assert.Null(SearchLinks.FromFields("2024", null, null, null, null).Filters.EventIds);
    }

    [Fact]
    public void Leaves_event_ids_out_of_the_question_translators_output_format_so_its_limits_hold()
    {
        var output = System.Text.Json.Nodes.JsonNode.Parse(QuestionPrompt.OutputSchemaJson)!;

        Assert.Null(output["properties"]!["filters"]!["properties"]!["eventIds"]);
        Assert.Contains("eventIds", QuestionPrompt.NotFromQuestions);
    }
}

/// <summary>Occasion types in the DSL, the counts, the chips and the words of the answers (S-024, D-094).</summary>
public sealed class OccasionTypeDslTests
{
    [Fact]
    public void Parses_event_types_and_counting_occasions()
    {
        Assert.True(SearchQueryJson.TryParse(
            """{"filters": {"eventTypes": ["wedding", "trip"]}, "aggregate": {"count": "events", "groupBy": "eventType"}}""", out var query, out var error), error);

        Assert.Equal(["wedding", "trip"], query.Filters.EventTypes);
        Assert.Equal((SearchCount.Events, SearchGroupBy.EventType), (query.Aggregate!.Count, query.Aggregate.GroupBy));
        Assert.True(SearchQueryJson.TryDecode(SearchQueryJson.Encode(query), out var decoded, out _));
        Assert.Equal(query, decoded);
        Assert.NotEqual(query, query with { Filters = query.Filters with { EventTypes = ["wedding"] } });
    }

    [Theory]
    [InlineData("""{"filters": {"eventTypes": ["funeral"]}}""", "filters.eventTypes")]
    [InlineData("""{"filters": {"eventTypes": ["Wedding"]}}""", "filters.eventTypes")]
    [InlineData("""{"aggregate": {"count": "events", "groupBy": "year"}}""", "eventType")]
    [InlineData("""{"aggregate": {"count": "assets", "groupBy": "eventType"}}""", "events")]
    [InlineData("""{"aggregate": {"count": "occasions"}}""", "aggregate.count")]
    public void Refuses_an_unknown_type_and_a_grouping_that_does_not_go_with_the_count(string json, string mentions)
    {
        Assert.False(SearchQueryJson.TryParse(json, out _, out var error));
        Assert.Contains(mentions, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Offers_the_types_to_a_question_and_counts_one_more_optional_property_within_the_limit()
    {
        var output = System.Text.Json.Nodes.JsonNode.Parse(QuestionPrompt.OutputSchemaJson)!;

        var types = output["properties"]!["filters"]!["properties"]!["eventTypes"]!;
        Assert.NotNull(types);
        Assert.Equal(OccasionTypes.All, types["items"]!["enum"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Equal(["assets", "events"], output["properties"]!["aggregate"]!["properties"]!["count"]!["enum"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Contains("eventType", output["properties"]!["aggregate"]!["properties"]!["groupBy"]!["enum"]!.AsArray().Select(c => c?.GetValue<string>()));
    }

    [Theory]
    [InlineData(Cultures.English, "Occasion type: Wedding, Trip")]
    [InlineData(Cultures.BrazilianPortuguese, "Tipo de ocasião: Casamento, Viagem")]
    public void Shows_the_types_as_a_chip_in_the_culture_that_removes_them(string culture, string label)
    {
        using var _ = Cultures.Use(culture);
        var query = new SearchQuery { Filters = new SearchFilters { EventTypes = ["wedding", "trip"], Years = [2026] } };

        var chip = SearchChips.For(query, Cultures.Text()).Single(c => c.Label == label);

        Assert.Equal(new SearchQuery { Filters = new SearchFilters { Years = [2026] } }, chip.Without);
    }

    [Theory]
    [InlineData(Cultures.English, "Count occasions", "Count by occasion type")]
    [InlineData(Cultures.BrazilianPortuguese, "Contar ocasiões", "Contagem por tipo de ocasião")]
    public void Labels_a_count_of_occasions_and_its_grouping(string culture, string total, string grouped)
    {
        using var _ = Cultures.Use(culture);
        var events = new SearchQuery { Aggregate = new SearchAggregate { Count = SearchCount.Events } };

        Assert.Equal([total], SearchChips.For(events, Cultures.Text()).Select(c => c.Label));
        Assert.Equal([grouped], SearchChips.For(events with { Aggregate = events.Aggregate with { GroupBy = SearchGroupBy.EventType } }, Cultures.Text()).Select(c => c.Label));
    }

    [Theory]
    [InlineData(Cultures.English, 4, "4 occasions of type Wedding in 2026")]
    [InlineData(Cultures.English, 1, "1 occasion of type Wedding in 2026")]
    [InlineData(Cultures.BrazilianPortuguese, 4, "4 ocasiões do tipo Casamento em 2026")]
    [InlineData(Cultures.BrazilianPortuguese, 1, "1 ocasião do tipo Casamento em 2026")]
    public void Says_how_many_occasions_of_a_type_in_the_culture(string culture, int count, string expected)
    {
        using var _ = Cultures.Use(culture);
        var query = new SearchQuery
        {
            Filters = new SearchFilters { Years = [2026], EventTypes = ["wedding"] },
            Aggregate = new SearchAggregate { Count = SearchCount.Events },
        };

        Assert.Equal(expected, CountAnswers.Sentence(query, count, Cultures.Text()));
    }

    [Fact]
    public void Ranks_the_types_with_links_to_their_photos_and_the_occasions_without_one_last_with_none()
    {
        var text = Cultures.Text();
        var query = new SearchQuery { Aggregate = new SearchAggregate { Count = SearchCount.Events, GroupBy = SearchGroupBy.EventType } };
        var count = new CountResult(7, [new CountGroup("wedding", 4), new CountGroup("trip", 1), new CountGroup(null, 2)], null);

        var rows = CountAnswers.Rows(query, count, text);

        Assert.Equal(["Wedding", "Trip", "No type"], rows.Select(r => r.Label));
        Assert.Equal([4, 1, 2], rows.Select(r => r.Count));
        // Occasions are counted, so a row goes to the occasions of its type; the ones without a type go nowhere.
        Assert.Equal(["occasions?type=wedding", "occasions?type=trip", null], rows.Select(r => r.Href));
        Assert.All(rows, r => Assert.Null(r.Items));
        Assert.Equal("Occasions by type", CountAnswers.Heading(query, text));
    }

    [Fact]
    public void A_row_of_a_ranking_with_another_filter_goes_to_the_photos_of_its_type_since_the_occasions_page_cannot_say_which_year()
    {
        var query = new SearchQuery
        {
            Filters = new SearchFilters { Years = [2026] },
            Aggregate = new SearchAggregate { Count = SearchCount.Events, GroupBy = SearchGroupBy.EventType },
        };

        var rows = CountAnswers.Rows(query, new CountResult(2, [new CountGroup("wedding", 2)], null), Cultures.Text());

        Assert.Null(rows[0].Href);
        Assert.Equal(new SearchQuery { Filters = new SearchFilters { Years = [2026], EventTypes = ["wedding"] } }, rows[0].Items);
    }

    [Theory]
    [InlineData(null, "occasions", "Count_ShowOccasions")]
    [InlineData("wedding", "occasions?type=wedding", "Count_ShowOccasions")]
    [InlineData("wedding,trip", null, "Count_ShowPhotos")]
    public void Shows_the_occasions_under_a_count_of_occasions_when_the_occasions_page_can_say_what_was_counted(string? types, string? href, string key)
    {
        var query = new SearchQuery
        {
            Filters = new SearchFilters { EventTypes = types?.Split(',') },
            Aggregate = new SearchAggregate { Count = SearchCount.Events },
        };

        var show = CountAnswers.Show(query);

        Assert.Equal(key, show.Key);
        Assert.Equal(href ?? SearchLinks.Timeline(query with { Aggregate = null }), show.Href);
    }

    [Fact]
    public void Shows_the_photos_when_a_count_of_occasions_has_other_filters_and_the_items_for_a_count_of_assets()
    {
        var withYear = new SearchQuery
        {
            Filters = new SearchFilters { EventTypes = ["wedding"], Years = [2026] },
            Aggregate = new SearchAggregate { Count = SearchCount.Events },
        };
        var assets = new SearchQuery { Filters = new SearchFilters { Years = [2026] }, Aggregate = new SearchAggregate() };

        Assert.Equal(("Count_ShowPhotos", SearchLinks.Timeline(withYear with { Aggregate = null })), (CountAnswers.Show(withYear).Key, CountAnswers.Show(withYear).Href));
        Assert.Equal("Count_ShowItems", CountAnswers.Show(assets).Key);
        Assert.Equal("Show the occasions", Cultures.Text()["Count_ShowOccasions"]);
        Assert.Equal("Show the photos", Cultures.Text()["Count_ShowPhotos"]);
    }

    [Theory]
    [InlineData(Cultures.English, "2 occasions of types Wedding, Trip")]
    [InlineData(Cultures.BrazilianPortuguese, "2 ocasiões dos tipos Casamento, Viagem")]
    public void Says_of_types_when_several_types_are_counted(string culture, string expected)
    {
        using var _ = Cultures.Use(culture);
        var query = new SearchQuery
        {
            Filters = new SearchFilters { EventTypes = ["wedding", "trip"] },
            Aggregate = new SearchAggregate { Count = SearchCount.Events },
        };

        Assert.Equal(expected, CountAnswers.Sentence(query, 2, Cultures.Text()));
    }

    [Fact]
    public void Tells_the_translator_all_the_occasion_types_before_any_occasion_is_typed_and_only_the_ones_there_are_after()
    {
        var today = new DateOnly(2026, 10, 22);
        var untyped = new FilterOptions([], [], []);
        var typed = new FilterOptions([], [], []) { EventTypes = ["wedding", "trip"] };

        Assert.Equal(
            [.. OccasionTypes.All.Order(StringComparer.Ordinal)],
            QuestionService.Context(untyped, today, "Europe/Lisbon").Lists.Single(l => l.DslField == "filters.eventTypes").Values);
        Assert.Equal(
            ["trip", "wedding"],
            QuestionService.Context(typed, today, "Europe/Lisbon").Lists.Single(l => l.DslField == "filters.eventTypes").Values);
    }

    [Fact]
    public void Refuses_a_size_for_a_count_of_occasions_since_an_occasion_has_none()
    {
        Assert.False(SearchQueryJson.TryParse("""{"aggregate": {"count": "events", "sum": "bytes"}}""", out _, out var error));
        Assert.Contains("aggregate.sum", error, StringComparison.Ordinal);
        Assert.Contains("events", error, StringComparison.Ordinal);
        Assert.True(SearchQueryJson.TryParse("""{"aggregate": {"count": "assets", "sum": "bytes"}}""", out _, out _));
    }

    [Fact]
    public void Writes_a_type_that_has_no_text_as_its_key()
    {
        Assert.Equal("picnic", Cultures.Text().EventTypeLabel("picnic"));
        Assert.All(OccasionTypes.All, type => Assert.False(Cultures.Text()["EventType_" + type].ResourceNotFound, type));
    }
}
