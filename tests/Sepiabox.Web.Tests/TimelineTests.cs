using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Web.Components.Pages;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

public sealed class TimelineTests : BunitContext
{
    [Fact]
    public void Assets_from_three_months_show_three_month_groups_newest_first()
    {
        Use(new FakeLibrary(
            new TimelineItem(Ids.Of(1), AssetKinds.Photo, new DateTime(2026, 8, 3, 10, 0, 0), true),
            new TimelineItem(Ids.Of(2), AssetKinds.Photo, new DateTime(2026, 10, 1, 9, 0, 0), true),
            new TimelineItem(Ids.Of(3), AssetKinds.Video, new DateTime(2025, 12, 24, 20, 0, 0), true)));

        var page = Render<Timeline>();

        var titles = page.FindAll("section.month h2").Select(h => h.TextContent).ToList();
        Assert.Equal(["outubro de 2026", "agosto de 2026", "dezembro de 2025"], titles);
        Assert.Equal(3, page.FindAll("ul.grid img").Count);
    }

    [Fact]
    public void An_asset_without_a_date_appears_last_under_sem_data()
    {
        Use(new FakeLibrary(
            new TimelineItem(Ids.Of(4), AssetKinds.Photo, null, true),
            new TimelineItem(Ids.Of(5), AssetKinds.Photo, new DateTime(2019, 7, 14, 10, 32, 0), true)));

        var page = Render<Timeline>();

        var groups = page.FindAll("section.month");
        Assert.Equal(2, groups.Count);
        Assert.Equal("Sem data", groups[^1].QuerySelector("h2")!.TextContent);
        Assert.Contains($"fotos/{Ids.Of(4)}", groups[^1].InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Thumbnails_point_at_the_signing_endpoint_and_the_page_holds_no_storage_links()
    {
        Use(new FakeLibrary(new TimelineItem(Ids.Of(6), AssetKinds.Photo, new DateTime(2026, 10, 2), true)));

        var page = Render<Timeline>();

        Assert.Equal($"fotos/{Ids.Of(6)}/thumb-256", page.Find("ul.grid img").GetAttribute("src"));
        Assert.DoesNotContain("sig=", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_full_page_links_to_the_older_page()
    {
        Use(new FakeLibrary(
            new TimelineItem(Ids.Of(7), AssetKinds.Photo, new DateTime(2026, 1, 2), false),
            new TimelineItem(Ids.Of(8), AssetKinds.Photo, new DateTime(2026, 1, 1), false))
        { PageSize = 1 });

        var page = Render<Timeline>();

        Assert.Equal("./?pagina=2", page.Find("nav.pages a").GetAttribute("href"));
        Assert.Contains("sem miniatura", page.Find("ul.grid .placeholder").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_library_says_how_to_add_photos()
    {
        Use(new FakeLibrary());

        var page = Render<Timeline>();

        Assert.Contains("sepiabox-ingest", page.Find("p.empty").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_beyond_the_end_says_there_are_no_more_photos()
    {
        Use(new FakeLibrary(new TimelineItem(Ids.Of(9), AssetKinds.Photo, new DateTime(2026, 1, 1), true)));

        var page = RenderPage(5);

        var message = page.Find("p.empty");
        Assert.Contains("Não há mais fotos", message.TextContent, StringComparison.Ordinal);
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
            new TimelineItem(Ids.Of(10), AssetKinds.Photo, new DateTime(2026, 1, 2), true),
            new TimelineItem(Ids.Of(11), AssetKinds.Photo, new DateTime(2026, 1, 1), true))
        { PageSize = 1 });

        var page = RenderPage(2);

        Assert.Equal($"fotos/{Ids.Of(11)}?pagina=2", page.Find("a.thumb").GetAttribute("href"));
    }

    [Fact]
    public void Paging_links_are_relative_and_keep_other_query_parameters()
    {
        Use(new FakeLibrary(
            new TimelineItem(Ids.Of(12), AssetKinds.Photo, new DateTime(2026, 1, 3), true),
            new TimelineItem(Ids.Of(13), AssetKinds.Photo, new DateTime(2026, 1, 2), true),
            new TimelineItem(Ids.Of(14), AssetKinds.Photo, new DateTime(2026, 1, 1), true))
        { PageSize = 1 });
        Services.GetRequiredService<NavigationManager>().NavigateTo("/?pagina=2&vista=grelha");

        var page = Render<Timeline>();

        Assert.Equal(["./?vista=grelha", "./?vista=grelha&pagina=3"], page.FindAll("nav.pages a").Select(a => a.GetAttribute("href")));
    }

    private void Use(FakeLibrary library) => Services.AddSingleton<ILibraryQueries>(library);

    // The page reads `pagina` from the URL, as a visitor's browser would send it.
    private IRenderedComponent<Timeline> RenderPage(object pagina)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/?pagina={pagina}");
        return Render<Timeline>();
    }
}
