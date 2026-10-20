using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.Equal("Lisbon", page.Find("select[name=city] option[selected]").TextContent);
        Assert.Equal("12", page.Find("select[name=month] option[selected]").GetAttribute("value"));
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
        Assert.Equal("On this day", shortcut.TextContent);
        Assert.Equal(link, shortcut.GetAttribute("href"));
        Assert.Equal("page", shortcut.GetAttribute("aria-current"));
        Assert.Equal($"photos/{Ids.Of(31)}?q={link["./?q=".Length..]}", page.Find("a.thumb").GetAttribute("href"));
    }

    [Fact]
    public void Offers_the_catalog_s_years_cities_and_sources()
    {
        Use(new FakeLibrary(LisbonInDecember) { Options = new FilterOptions([2021, 2019], ["Lisbon", "Porto"], ["HD da Ana", "HD do Jonathas"]) });

        var page = Render<Timeline>();

        Assert.Equal(["", "2021", "2019"], page.FindAll("select[name=year] option").Select(o => o.GetAttribute("value")));
        Assert.Equal(["", "Lisbon", "Porto"], page.FindAll("select[name=city] option").Select(o => o.GetAttribute("value")));
        Assert.Equal(13, page.FindAll("select[name=month] option").Count);
        Assert.Equal("December", page.FindAll("select[name=month] option")[12].TextContent);
        Assert.Equal("get", page.Find("form.filters").GetAttribute("method"));
        Assert.Empty(page.FindAll("a.clear"));
    }

    [Fact]
    public void Speaks_portuguese_in_pt_br()
    {
        using var culture = Cultures.Use(Cultures.BrazilianPortuguese);
        Use(new FakeLibrary(LisbonInDecember));

        var page = Render<Timeline>();

        Assert.Equal("Neste dia", page.Find("a.shortcut").TextContent);
        Assert.Equal("Filtrar", page.Find("form.filters button").TextContent);
        Assert.Equal("Dezembro", page.FindAll("select[name=month] option")[12].TextContent);
    }

    [Fact]
    public void Shows_no_filter_bar_when_the_library_is_empty()
    {
        Use(new FakeLibrary());

        var page = Render<Timeline>();

        Assert.Empty(page.FindAll("form.filters"));
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
