using AngleSharp.Html.Parser;
using Sepiabox.Web.Components.Shared;

namespace Sepiabox.Web.Tests;

/// <summary>The parts every page shares (S-069, D-119): the head, the callout, the section title, the panel and the notice.</summary>
public sealed class SharedPageComponentsTests : LocalizedContext
{
    [Fact]
    public void Draws_the_head_with_the_way_back_the_line_under_the_title_and_the_actions()
    {
        var head = Render<PageHead>(p => p
            .Add(h => h.Title, "People")
            .Add(h => h.Back, "<a href=\"./\">Back</a>")
            .Add(h => h.Subtitle, "Found by face.")
            .AddChildContent("<a class=\"button\" href=\"x\">Go</a>"));

        Assert.Equal("People", head.Find("div.page-head h1.page-heading").TextContent);
        Assert.Equal("Back", head.Find("p.back a").TextContent);
        Assert.Equal("Found by face.", head.Find("p.page-sub").TextContent);
        Assert.Equal("Go", head.Find(".page-head-actions a.button").TextContent);
    }

    [Fact]
    public void Leaves_out_the_way_back_the_line_and_the_actions_when_there_are_none()
    {
        var head = Render<PageHead>(p => p.Add(h => h.Title, "Places"));

        Assert.Empty(head.FindAll("p.back"));
        Assert.Empty(head.FindAll(".page-sub"));
        Assert.Empty(head.FindAll(".page-head-actions"));
    }

    [Fact]
    public void Shows_the_count_of_a_section_with_thousands_separated()
    {
        var title = Render<SectionTitle>(p => p.Add(s => s.Title, "Named").Add(s => s.Count, 1234));

        Assert.Equal("Named", title.Find(".section-title h2").TextContent);
        Assert.Equal("1,234", title.Find(".section-title .n").TextContent);
        Assert.Empty(Render<SectionTitle>(p => p.Add(s => s.Title, "Named")).FindAll(".n"));
    }

    [Fact]
    public void Marks_a_callout_as_a_note_and_a_notice_as_a_status()
    {
        Assert.Equal("Name them.", Render<Callout>(p => p.AddChildContent("Name them.")).Find("div.callout[role=note] span").TextContent);

        var notice = Render<Notice>(p => p.Add(n => n.Class, "person-note").AddChildContent("Saved."));
        Assert.Equal("Saved.", notice.Find("p.note.person-note[role=status]").TextContent);
    }

    [Fact]
    public void Draws_a_panel_labelled_by_its_heading_and_keeps_the_pages_own_class()
    {
        var panel = Render<Panel>(p => p
            .Add(s => s.Title, "Totals")
            .Add(s => s.HeadingId, "totals-heading")
            .Add(s => s.Class, "storage-totals")
            .AddChildContent("<p>Body</p>"));

        var section = panel.Find("section.panel.block.storage-totals");
        Assert.Equal("totals-heading", section.GetAttribute("aria-labelledby"));
        Assert.Equal("Totals", panel.Find("h2#totals-heading").TextContent);
    }

    [Fact]
    public void Draws_a_panel_without_a_label_when_it_has_no_title()
    {
        var panel = Render<Panel>(p => p.AddChildContent("<p>Body</p>"));

        Assert.Null(panel.Find("section.panel").GetAttribute("aria-labelledby"));
        Assert.Empty(panel.FindAll("h2"));
    }
}

/// <summary>Every page that moved onto the shared parts, over HTTP: the head with its heading, once, and no old heading markup.</summary>
public sealed class SharedPageHeadPagesTests
{
    [Theory]
    [InlineData("/people")]
    [InlineData("/occasions")]
    [InlineData("/places")]
    [InlineData("/storage")]
    [InlineData("/sources")]
    [InlineData("/settings")]
    public async Task Shows_the_shared_head_on_the_page(string route)
    {
        await using var web = new WebFactory(new FakeLibrary());
        using var http = web.CreateSignedInClient();

        var html = await http.GetStringAsync(new Uri(route, UriKind.Relative));

        var main = new HtmlParser().ParseDocument(html).QuerySelector("main")!;
        Assert.Single(main.QuerySelectorAll("div.page-head h1.page-heading"));
        Assert.Single(main.QuerySelectorAll("h1"));
    }
}
