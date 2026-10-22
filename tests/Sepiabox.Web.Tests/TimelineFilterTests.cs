using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Catalog.Search;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The filter bar and filtered timeline (S-013), rendered with an in-memory library.</summary>
public sealed class TimelineFilterTests : LocalizedContext
{
    private static readonly TimelineItem LisbonInDecember =
        new(Ids.Of(31), AssetKinds.Photo, new DateTime(2019, 12, 24, 20, 0, 0), true, DurationSeconds: null);

    [Fact]
    public void Searches_with_the_filters_in_the_address_and_shows_only_what_matches()
    {
        var library = Use(new FakeLibrary(new TimelineItem(Ids.Of(30), AssetKinds.Photo, new DateTime(2019, 7, 1), true, null))
        {
            SearchResults = new SearchPage([LisbonInDecember], HasMore: false),
        });
        var query = SearchLinks.FromFields(null, "12", null, "Lisbon", null);

        var page = RenderAt(SearchLinks.Timeline(query));

        Assert.Equal(query, Assert.Single(library.Searches));
        Assert.Null(library.RequestedPage);
        Assert.Equal(["December 2019"], page.FindAll("section.month h2").Select(h => h.TextContent));
        Assert.Single(page.FindAll("ul.grid li"));
        Assert.Equal("Lisbon", page.Find("input[name=city][checked]").ParentElement!.TextContent.Trim());
        Assert.Equal("12", page.Find("input[name=month][checked]").GetAttribute("value"));
        Assert.Equal("./", page.Find("a.clear").GetAttribute("href"));
    }

    [Fact]
    public void Applies_the_same_filters_when_a_copied_address_is_opened()
    {
        var library = Use(new FakeLibrary { SearchResults = new SearchPage([LisbonInDecember], false) });
        var navigation = Services.GetRequiredService<NavigationManager>();
        // The bar's form submits its fields; the page answers with the one address for them.
        navigation.NavigateTo("/?filter=1&year=&month=12&kind=&city=Lisbon&source=");
        Render<Timeline>();
        var address = navigation.Uri;

        // The address, copied and opened again.
        navigation.NavigateTo(address);
        Render<Timeline>();

        Assert.Contains("?q=", address, StringComparison.Ordinal);
        Assert.DoesNotContain("city=", address, StringComparison.Ordinal);
        // Once when the bar's address became ?q=, once when the copy was opened: the same filters both times.
        Assert.Equal(2, library.Searches.Count);
        Assert.All(library.Searches, search => Assert.Equal(SearchLinks.FromFields(null, "12", null, "Lisbon", null), search));
    }

    [Fact]
    public void Says_the_filters_could_not_be_read_when_the_address_is_tampered_with()
    {
        var library = Use(new FakeLibrary(LisbonInDecember));

        var page = RenderAt("/?q=not-a-search");

        Assert.Contains("These filters could not be read.", page.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.Empty(library.Searches);
        Assert.Empty(page.FindAll("ul.grid li"));
    }

    [Fact]
    public void Says_nothing_matches_when_the_search_finds_nothing()
    {
        Use(new FakeLibrary(LisbonInDecember));

        var page = RenderAt(SearchLinks.Timeline(SearchLinks.FromFields("1999", null, null, null, null)));

        Assert.Contains("Nothing matches these filters.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_only_the_first_results_are_shown_when_more_match()
    {
        Use(new FakeLibrary { SearchResults = new SearchPage([LisbonInDecember], HasMore: true) });

        var page = RenderAt(SearchLinks.Timeline(SearchLinks.FromFields("2019", null, null, null, null)));

        Assert.Contains("Showing the first 1 results.", page.Find("p.more").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Links_on_this_day_and_details_that_return_to_the_same_filters()
    {
        Use(new FakeLibrary { SearchResults = new SearchPage([LisbonInDecember], false) });
        var link = SearchLinks.Timeline(SearchLinks.OnThisDay);

        var page = RenderAt(link);

        var shortcut = page.Find("a.shortcut");
        Assert.Equal("On this day", shortcut.TextContent.Trim());
        Assert.Equal(link, shortcut.GetAttribute("href"));
        Assert.Equal("page", shortcut.GetAttribute("aria-current"));
        Assert.Equal($"photos/{Ids.Of(31)}?q={link["./?q=".Length..]}", page.Find("a.thumb").GetAttribute("href"));
    }

    [Fact]
    public void Offers_the_catalog_s_years_cities_and_sources()
    {
        Use(new FakeLibrary(LisbonInDecember) { Options = new FilterOptions([2021, 2019], ["Lisbon", "Porto"], ["HD da Ana", "HD do Jonathas"]) });

        var page = Render<Timeline>();

        Assert.Equal(["", "2021", "2019"], page.FindAll("input[name=year]").Select(o => o.GetAttribute("value")));
        Assert.Equal(["", "Lisbon", "Porto"], page.FindAll("input[name=city]").Select(o => o.GetAttribute("value")));
        Assert.Equal(13, page.FindAll("input[name=month]").Count);
        Assert.Equal("December", page.FindAll("input[name=month]")[12].ParentElement!.TextContent.Trim());
        Assert.Equal("get", page.Find("form.filters").GetAttribute("method"));
        Assert.Empty(page.FindAll("a.clear"));
    }

    [Fact]
    public void Speaks_portuguese_in_pt_br()
    {
        using var culture = Cultures.Use(Cultures.BrazilianPortuguese);
        Use(new FakeLibrary(LisbonInDecember));

        var page = Render<Timeline>();

        Assert.Equal("Neste dia", page.Find("a.shortcut").TextContent.Trim());
        Assert.Equal("Filtrar", page.Find("form.filters > button.button").TextContent);
        Assert.Equal("Dezembro", page.FindAll("input[name=month]")[12].ParentElement!.TextContent.Trim());
    }

    [Fact]
    public void Shows_no_filter_bar_when_the_library_is_empty()
    {
        Use(new FakeLibrary());

        var page = Render<Timeline>();

        Assert.Empty(page.FindAll("form.filters"));
    }

    [Fact]
    public void Hides_the_description_box_and_keeps_the_other_filters_when_the_text_model_is_missing()
    {
        // LocalizedContext registers an unavailable embedder, as a web without the CLIP text model.
        var library = Use(new FakeLibrary(LisbonInDecember) { SearchResults = new SearchPage([LisbonInDecember], false) });

        var page = RenderAt(SearchLinks.Timeline(SearchLinks.FromFields(null, "12", null, "Lisbon", null)));

        Assert.Empty(page.FindAll("input[name=text]"));
        Assert.NotEmpty(page.FindAll("input[name=city]"));
        Assert.Single(library.Searches);
        Assert.Single(page.FindAll("ul.grid li"));
    }

    [Fact]
    public void Shows_the_description_box_in_english_when_the_text_model_is_there()
    {
        Services.AddSingleton<IQueryEmbedder>(new FakeQueryEmbedder());
        Use(new FakeLibrary(LisbonInDecember));

        var page = Render<Timeline>();

        var box = page.Find("input[name=text]");
        Assert.Equal("Describe (in English)", page.Find("label.describe span").TextContent);
        Assert.Equal("en", box.GetAttribute("lang"));
        Assert.Equal("300", box.GetAttribute("maxlength"));
    }

    [Fact]
    public void Says_describe_in_english_in_portuguese_in_pt_br()
    {
        using var culture = Cultures.Use(Cultures.BrazilianPortuguese);
        Services.AddSingleton<IQueryEmbedder>(new FakeQueryEmbedder());
        Use(new FakeLibrary(LisbonInDecember));

        var page = Render<Timeline>();

        Assert.Equal("Descrever (em inglês)", page.Find("label.describe span").TextContent);
    }

    [Fact]
    public void Lists_description_results_most_similar_first_without_month_groups()
    {
        Services.AddSingleton<IQueryEmbedder>(new FakeQueryEmbedder());
        TimelineItem[] ranked =
        [
            new(Ids.Of(51), AssetKinds.Photo, new DateTime(2015, 1, 1), true, null),
            new(Ids.Of(52), AssetKinds.Photo, new DateTime(2021, 1, 1), true, null),
            new(Ids.Of(53), AssetKinds.Photo, null, true, null),
        ];
        var library = Use(new FakeLibrary { SearchResults = new SearchPage(ranked, false) });

        var page = RenderAt(SearchLinks.Timeline(SearchLinks.FromFields(null, null, null, null, null, "beach at sunset")));

        Assert.Equal("beach at sunset", Assert.Single(library.Searches).Filters.Visual!.Text);
        Assert.Equal(["Closest to the description"], page.FindAll("section.month h2").Select(h => h.TextContent));
        Assert.Equal([Ids.Of(51), Ids.Of(52), Ids.Of(53)], page.FindAll("a.thumb").Select(a => a.GetAttribute("href")![7..71]));
        Assert.Equal("beach at sunset", page.Find("input[name=text]").GetAttribute("value"));
    }

    [Fact]
    public void Says_search_by_description_is_unavailable_instead_of_failing()
    {
        Use(new FakeLibrary(LisbonInDecember) { SearchFailure = new VisualSearchUnavailableException("No model.") });

        var page = RenderAt(SearchLinks.Timeline(SearchLinks.FromFields(null, null, null, null, null, "snow")));

        Assert.Contains("Search by description is not available right now.", page.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll("form.filters"));
    }

    [Fact]
    public void Shows_the_photos_similar_to_one_most_similar_first_without_month_groups()
    {
        var older = new TimelineItem(Ids.Of(32), AssetKinds.Photo, new DateTime(2018, 1, 1), true, null);
        var library = Use(new FakeLibrary { SearchResults = new SearchPage([older, LisbonInDecember], HasMore: false) });
        var query = SearchLinks.Similar(Ids.Of(30));

        var page = RenderAt(SearchLinks.Timeline(query));

        Assert.Equal(query, Assert.Single(library.Searches));
        Assert.Equal(["Most similar to the photo"], page.FindAll("section.month h2").Select(h => h.TextContent));
        // In the order the search gave, not by date.
        Assert.Equal([$"photos/{Ids.Of(32)}", $"photos/{Ids.Of(31)}"], page.FindAll("ul.grid a").Select(a => a.GetAttribute("href")![..71]));
        Assert.Contains("Similar to a photo", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_the_photo_has_not_been_analysed_yet_when_a_search_for_similar_ones_finds_no_embedding()
    {
        Use(new FakeLibrary { SearchFailure = new AssetNotAnalysedException(Ids.Of(30), AssetKinds.Photo) });

        var page = RenderAt(SearchLinks.Timeline(SearchLinks.Similar(Ids.Of(30))));

        Assert.Contains("This photo has not been analysed yet.", page.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("ul.grid"));
    }

    [Fact]
    public void Says_the_photo_is_not_in_the_library_when_a_search_for_similar_ones_names_no_asset()
    {
        Use(new FakeLibrary { SearchFailure = new AssetNotAnalysedException(Ids.Of(30), kind: null) });

        var page = RenderAt(SearchLinks.Timeline(SearchLinks.Similar(Ids.Of(30))));

        Assert.Contains("This photo or video is not in the library.", page.Find("[role=alert]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_the_video_has_not_been_analysed_yet_when_it_is_a_video()
    {
        Use(new FakeLibrary { SearchFailure = new AssetNotAnalysedException(Ids.Of(30), AssetKinds.Video) });

        var page = RenderAt(SearchLinks.Timeline(SearchLinks.Similar(Ids.Of(30))));

        Assert.Contains("This video has not been analysed yet.", page.Find("[role=alert]").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Cultures.English, "Show more")]
    [InlineData(Cultures.BrazilianPortuguese, "Mostrar mais")]
    public void Offers_more_similar_ones_instead_of_saying_to_narrow_the_filters(string culture, string label)
    {
        using var language = Cultures.Use(culture);
        Use(new FakeLibrary { SearchResults = new SearchPage([LisbonInDecember], HasMore: true) });
        var query = SearchLinks.Similar(Ids.Of(30));

        var page = RenderAt(SearchLinks.Timeline(query));

        var more = page.Find("p.more a");
        Assert.Equal(label, more.TextContent);
        Assert.Equal(SearchLinks.Timeline(SimilarSearch.More(query)), more.GetAttribute("href"));
        Assert.Single(page.FindAll("p.more"));
        Assert.DoesNotContain("Narrow the filters", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Refine os filtros", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Shows_no_link_and_no_note_once_it_shows_as_many_similar_ones_as_allowed()
    {
        Use(new FakeLibrary { SearchResults = new SearchPage([LisbonInDecember], HasMore: true) });
        var query = new SearchQuery
        {
            Filters = new SearchFilters { SimilarTo = new SimilarToFilter { AssetId = Ids.Of(30), Limit = SearchQuery.MaxLimit } },
            Limit = SearchQuery.MaxLimit,
        };

        var page = RenderAt(SearchLinks.Timeline(query));

        Assert.Single(page.FindAll("ul.grid li"));
        Assert.Empty(page.FindAll("p.more"));
        Assert.DoesNotContain("First 1000", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Narrows_the_similar_ones_with_the_bar_instead_of_starting_a_new_search()
    {
        var library = Use(new FakeLibrary { SearchResults = new SearchPage([LisbonInDecember], false) });
        var similar = SearchLinks.Similar(Ids.Of(30));

        var page = RenderAt(SearchLinks.Timeline(similar));
        var hidden = page.FindAll("form.filters input[type=hidden]").ToDictionary(i => i.GetAttribute("name")!, i => i.GetAttribute("value"));
        Assert.Equal(Ids.Of(30), hidden[SearchLinks.SimilarParameter]);
        Assert.False(hidden.ContainsKey(SearchLinks.SimilarLimitParameter));
        // The bar's form submitted with a year chosen.
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/?filter=1&year=2020&month=&kind=&city=&source=&{SearchLinks.SimilarParameter}={Ids.Of(30)}");
        Render<Timeline>();

        Assert.Equal(similar with { Filters = similar.Filters with { Years = [2020] } }, library.Searches[^1]);
        // The chip still removes it.
        Assert.Contains(SearchChips.For(library.Searches[^1], Cultures.Text()), c => c.Label == "Similar to a photo" && c.Without.Filters.SimilarTo is null);
    }

    private FakeLibrary Use(FakeLibrary library)
    {
        Services.AddSingleton<ILibraryQueries>(library);
        return library;
    }

    private IRenderedComponent<Timeline> RenderAt(string link)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(link.StartsWith("./", StringComparison.Ordinal) ? link[1..] : link);
        return Render<Timeline>();
    }
}
