using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Sepiabox.Web.Components.Layout;

namespace Sepiabox.Web.Tests;

/// <summary>The header's links and its "More" disclosure (issue 90, D-102).</summary>
public sealed class HeaderMoreMenuTests : LocalizedContext
{
    public HeaderMoreMenuTests()
    {
        AddAuthorization().SetAuthorized("ana@example.com");
        Services.AddSingleton<AntiforgeryStateProvider>(new FixedAntiforgery());
    }

    [Fact]
    public void Keeps_the_pages_of_every_day_in_the_header_and_the_others_in_a_closed_disclosure_without_script()
    {
        var header = Open("/").Find("header");

        var more = header.QuerySelector("details.more")!;
        Assert.False(more.HasAttribute("open"));
        Assert.Equal(["organize", "places", "sources", "storage"], more.QuerySelectorAll("a.nav-link").Select(a => a.GetAttribute("href")));
        var visible = header.QuerySelectorAll("a.nav-link").Where(a => !more.Contains(a)).Select(a => a.GetAttribute("href"));
        Assert.Equal(["occasions", "people", "settings"], visible);
        Assert.Equal("More", more.QuerySelector("summary")!.TextContent.Trim(' ', ' ', '\n'));
    }

    [Theory]
    [InlineData(Cultures.English, "More", "Organize", "Places", "Sources", "Storage")]
    [InlineData(Cultures.BrazilianPortuguese, "Mais", "Organizar", "Lugares", "Origens", "Armazenamento")]
    public void Names_the_disclosure_and_its_pages_in_the_culture(string culture, string more, string organize, string places, string sources, string storage)
    {
        using var _ = Cultures.Use(culture);

        var header = Open("/").Find("header");

        Assert.Equal(more, header.QuerySelector("summary")!.TextContent.Trim());
        Assert.Equal([organize, places, sources, storage], header.QuerySelectorAll(".more-list a").Select(a => a.TextContent));
    }

    [Theory]
    [InlineData("/occasions", "occasions")]
    [InlineData("/people/3", "people")]
    [InlineData("/people?x=1", "people")]
    [InlineData("/settings", "settings")]
    public void Marks_the_page_being_shown_among_the_visible_links_and_leaves_the_disclosure_unmarked(string address, string href)
    {
        var header = Open(address).Find("header");

        Assert.Equal(href, Assert.Single(header.QuerySelectorAll("a[aria-current=page]")).GetAttribute("href"));
        Assert.DoesNotContain("more-active", header.QuerySelector("summary")!.ClassList);
        Assert.Empty(header.QuerySelectorAll(".more-current"));
    }

    [Theory]
    [InlineData(Cultures.English, "/places", "places", "More: Places")]
    [InlineData(Cultures.English, "/storage", "storage", "More: Storage")]
    [InlineData(Cultures.BrazilianPortuguese, "/organize", "organize", "Mais: Organizar")]
    [InlineData(Cultures.English, "/sources", "sources", "More: Sources")]
    public void Says_on_the_closed_disclosure_when_the_page_being_shown_is_inside_it(string culture, string address, string href, string summary)
    {
        using var _ = Cultures.Use(culture);

        var header = Open(address).Find("header");

        Assert.Equal(href, Assert.Single(header.QuerySelectorAll("a[aria-current=page]")).GetAttribute("href"));
        Assert.Contains("more-active", header.QuerySelector("summary")!.ClassList);
        Assert.Equal(summary, header.QuerySelector("summary")!.TextContent.Replace("\n", string.Empty).Trim());
    }

    [Fact]
    public void Marks_nothing_on_a_page_that_is_not_in_the_header_and_on_a_page_that_only_starts_like_one()
    {
        Assert.Empty(Open("/").FindAll("header a[aria-current]"));
        Assert.Empty(Open("/peoples").FindAll("header a[aria-current]"));
    }

    private IRenderedComponent<MainLayout> Open(string address)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(address);
        return Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "page"))));
    }

    private sealed class FixedAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }
}
