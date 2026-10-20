using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class TimelineTests : LocalizedContext
{
    [Fact]
    public void Assets_from_three_months_show_three_month_groups_newest_first()
    {
        Use(new FakeLibrary(
            new TimelineItem(Ids.Of(1), AssetKinds.Photo, new DateTime(2026, 8, 3, 10, 0, 0), true, DurationSeconds: null),
            new TimelineItem(Ids.Of(2), AssetKinds.Photo, new DateTime(2026, 10, 1, 9, 0, 0), true, DurationSeconds: null),
            new TimelineItem(Ids.Of(3), AssetKinds.Video, new DateTime(2025, 12, 24, 20, 0, 0), true, DurationSeconds: null)));

        var page = Render<Timeline>();

        var titles = page.FindAll("section.month h2").Select(h => h.TextContent).ToList();
        Assert.Equal(["October 2026", "August 2026", "December 2025"], titles);
        Assert.Equal(3, page.FindAll("ul.grid img").Count);
    }

    [Fact]
    public void A_3_second_video_shows_a_play_icon_and_0_03_on_its_card()
    {
        Use(new FakeLibrary(
            new TimelineItem(Ids.Of(15), AssetKinds.Video, new DateTime(2020, 8, 9, 18, 5, 0), true, DurationSeconds: 3),
            new TimelineItem(Ids.Of(16), AssetKinds.Photo, new DateTime(2020, 8, 8), true, DurationSeconds: null)));

        var page = Render<Timeline>();

        var cards = page.FindAll("a.thumb");
        Assert.Equal($"photos/{Ids.Of(15)}", cards[0].GetAttribute("href"));
        Assert.Equal("0:03", cards[0].QuerySelector(".badge")!.TextContent);
        Assert.NotNull(cards[0].QuerySelector(".play"));
        Assert.Null(cards[1].QuerySelector(".play"));
    }

    [Fact]
    public void An_asset_without_a_date_appears_last_under_no_date()
    {
        Use(new FakeLibrary(
            new TimelineItem(Ids.Of(4), AssetKinds.Photo, null, true, DurationSeconds: null),
            new TimelineItem(Ids.Of(5), AssetKinds.Photo, new DateTime(2019, 7, 14, 10, 32, 0), true, DurationSeconds: null)));

        var page = Render<Timeline>();

        var groups = page.FindAll("section.month");
        Assert.Equal(2, groups.Count);
        Assert.Equal("No date", groups[^1].QuerySelector("h2")!.TextContent);
        Assert.Contains($"photos/{Ids.Of(4)}", groups[^1].InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Thumbnails_point_at_the_signing_endpoint_and_the_page_holds_no_storage_links()
    {
        Use(new FakeLibrary(new TimelineItem(Ids.Of(6), AssetKinds.Photo, new DateTime(2026, 10, 2), true, DurationSeconds: null)));

        var page = Render<Timeline>();

        Assert.Equal($"photos/{Ids.Of(6)}/thumb-256", page.Find("ul.grid img").GetAttribute("src"));
        Assert.DoesNotContain("sig=", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_full_page_links_to_the_older_page()
    {
        Use(new FakeLibrary(
            new TimelineItem(Ids.Of(7), AssetKinds.Photo, new DateTime(2026, 1, 2), false, DurationSeconds: null),
            new TimelineItem(Ids.Of(8), AssetKinds.Photo, new DateTime(2026, 1, 1), false, DurationSeconds: null))
        { PageSize = 1 });

        var page = Render<Timeline>();

        Assert.Equal("./?page=2", page.Find("nav.pages a").GetAttribute("href"));
        Assert.Equal("Older →", page.Find("nav.pages a").TextContent);
        Assert.Equal("Pages", page.Find("nav.pages").GetAttribute("aria-label"));
        Assert.Contains("without a thumbnail", page.Find("ul.grid .placeholder").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_library_says_how_to_add_photos()
    {
        Use(new FakeLibrary());

        var page = Render<Timeline>();

        Assert.Equal("No photos yet. Use sepiabox-ingest to upload the ones on the external drive.", page.Find("p.empty").TextContent);
        Assert.Equal("Photos and videos", page.Find("h1").TextContent);
    }

    [Fact]
    public void A_page_beyond_the_end_says_there_are_no_more_photos()
    {
        Use(new FakeLibrary(new TimelineItem(Ids.Of(9), AssetKinds.Photo, new DateTime(2026, 1, 1), true, DurationSeconds: null)));

        var page = RenderPage(5);

        var message = page.Find("p.empty");
        Assert.Equal("No more photos. Back to the start", message.TextContent);
        Assert.Equal("./", message.QuerySelector("a")!.GetAttribute("href"));
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("-3", 1)]
    [InlineData("2147483647", LibraryQueries.MaxPage)]
    [InlineData("99999999999", LibraryQueries.MaxPage)]
    [InlineData("abc", 1)]
    [InlineData("", 1)]
    public void Out_of_range_or_malformed_page_numbers_are_clamped(string requested, int expected)
    {
        var library = new FakeLibrary();
        Use(library);

        RenderPage(requested);

        Assert.Equal(expected, library.RequestedPage);
    }

    [Fact]
    public void Photos_on_later_pages_link_back_to_their_page()
    {
        Use(new FakeLibrary(
            new TimelineItem(Ids.Of(10), AssetKinds.Photo, new DateTime(2026, 1, 2), true, DurationSeconds: null),
            new TimelineItem(Ids.Of(11), AssetKinds.Photo, new DateTime(2026, 1, 1), true, DurationSeconds: null))
        { PageSize = 1 });

        var page = RenderPage(2);

        Assert.Equal($"photos/{Ids.Of(11)}?page=2", page.Find("a.thumb").GetAttribute("href"));
    }

    [Fact]
    public void Paging_links_are_relative_and_keep_other_query_parameters()
    {
        Use(new FakeLibrary(
            new TimelineItem(Ids.Of(12), AssetKinds.Photo, new DateTime(2026, 1, 3), true, DurationSeconds: null),
            new TimelineItem(Ids.Of(13), AssetKinds.Photo, new DateTime(2026, 1, 2), true, DurationSeconds: null),
            new TimelineItem(Ids.Of(14), AssetKinds.Photo, new DateTime(2026, 1, 1), true, DurationSeconds: null))
        { PageSize = 1 });
        Services.GetRequiredService<NavigationManager>().NavigateTo("/?page=2&vista=grelha");

        var page = Render<Timeline>();

        Assert.Equal(["./?vista=grelha", "./?vista=grelha&page=3"], page.FindAll("nav.pages a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public void Month_headings_and_the_no_date_group_are_in_brazilian_portuguese_when_the_culture_is_pt_br()
    {
        using var culture = Cultures.Use(Cultures.BrazilianPortuguese);
        Use(new FakeLibrary(
            new TimelineItem(Ids.Of(20), AssetKinds.Photo, new DateTime(2019, 7, 14, 10, 32, 0), true, DurationSeconds: null),
            new TimelineItem(Ids.Of(21), AssetKinds.Photo, null, true, DurationSeconds: null)));

        var page = Render<Timeline>();

        Assert.Equal(["julho de 2019", "Sem data"], page.FindAll("section.month h2").Select(h => h.TextContent));
        Assert.Equal("Fotos e vídeos", page.Find("h1").TextContent);
        Assert.Equal("Páginas", page.Find("nav.pages").GetAttribute("aria-label"));
    }

    [Fact]
    public void July_2019_is_headed_july_2019_in_english()
    {
        Use(new FakeLibrary(new TimelineItem(Ids.Of(22), AssetKinds.Photo, new DateTime(2019, 7, 14, 10, 32, 0), true, DurationSeconds: null)));

        var page = Render<Timeline>();

        Assert.Equal("July 2019", page.Find("section.month h2").TextContent);
    }

    [Fact]
    public void The_empty_and_end_of_list_messages_are_in_brazilian_portuguese_when_the_culture_is_pt_br()
    {
        using var culture = Cultures.Use(Cultures.BrazilianPortuguese);
        Use(new FakeLibrary(new TimelineItem(Ids.Of(23), AssetKinds.Photo, new DateTime(2026, 1, 1), true, DurationSeconds: null)));

        var beyond = RenderPage(5);

        Assert.Equal("Não há mais fotos. Voltar ao início", beyond.Find("p.empty").TextContent);
    }

    [Fact]
    public void An_empty_library_says_how_to_add_photos_in_brazilian_portuguese_when_the_culture_is_pt_br()
    {
        using var culture = Cultures.Use(Cultures.BrazilianPortuguese);
        Use(new FakeLibrary());

        var page = Render<Timeline>();

        Assert.Equal("Ainda não há fotos. Use sepiabox-ingest para enviar as do HD externo.", page.Find("p.empty").TextContent);
    }

    [Fact]
    public void Paging_links_are_in_brazilian_portuguese_when_the_culture_is_pt_br()
    {
        using var culture = Cultures.Use(Cultures.BrazilianPortuguese);
        Use(new FakeLibrary(
            new TimelineItem(Ids.Of(24), AssetKinds.Photo, new DateTime(2026, 1, 3), true, DurationSeconds: null),
            new TimelineItem(Ids.Of(25), AssetKinds.Photo, new DateTime(2026, 1, 2), true, DurationSeconds: null),
            new TimelineItem(Ids.Of(26), AssetKinds.Photo, new DateTime(2026, 1, 1), true, DurationSeconds: null))
        { PageSize = 1 });

        var page = RenderPage(2);

        Assert.Equal(["← Mais recentes", "Mais antigas →"], page.FindAll("nav.pages a").Select(a => a.TextContent));
    }

    private void Use(FakeLibrary library) => Services.AddSingleton<ILibraryQueries>(library);

    // The page reads `page` from the URL, as a visitor's browser would send it.
    private IRenderedComponent<Timeline> RenderPage(object page)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/?page={page}");
        return Render<Timeline>();
    }
}
