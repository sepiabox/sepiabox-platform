using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Sepiabox.Web.Components.Parts;

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
        Assert.Equal("Found by face.", head.Find(".page-sub").TextContent);
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

    [Theory]
    [InlineData("success", "status")]
    [InlineData("info", "status")]
    [InlineData("warning", "status")]
    [InlineData("error", "alert")]
    public void Draws_a_notice_in_three_kinds_with_an_icon_and_the_role_that_fits(string variant, string role)
    {
        var notice = Render<Notice>(p => p.Add(n => n.Variant, variant).AddChildContent("Text."));

        var note = notice.Find($"p.note.note-{variant}");
        Assert.Equal(role, note.GetAttribute("role"));
        Assert.Single(note.QuerySelectorAll("svg.icon"));
        Assert.Equal("Text.", note.QuerySelector("span")!.TextContent);
    }

    [Fact]
    public void Draws_an_empty_state_with_an_icon_a_title_a_line_of_help_and_the_action()
    {
        var state = Render<EmptyState>(p => p
            .Add(e => e.Title, "No places yet")
            .Add(e => e.Action, "<a class=\"button\" href=\"places\">Add one</a>")
            .AddChildContent("Save the places you go back to."));

        Assert.Equal("No places yet", state.Find(".empty-state h2.empty-title").TextContent);
        Assert.Equal("Save the places you go back to.", state.Find("p.empty-help").TextContent);
        Assert.Equal("Add one", state.Find(".empty-action a.button").TextContent);
        Assert.Equal("true", state.Find("svg.empty-icon").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Leaves_out_the_help_and_the_action_of_an_empty_state_when_there_are_none()
    {
        var state = Render<EmptyState>(p => p.Add(e => e.Title, "Nothing"));

        Assert.Empty(state.FindAll(".empty-help"));
        Assert.Empty(state.FindAll(".empty-action"));
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

/// <summary>The stylesheet's shared rules (S-069, D-126): form primitives, the danger button and the desktop width.</summary>
public sealed class SharedStylesheetTests
{
    private static string Css { get; } = File.ReadAllText(Path.Combine(SourceTree.WebProject, "wwwroot", "app.css"));

    [Theory]
    [InlineData(".field {")]
    [InlineData(".field-help {")]
    [InlineData(".field-error {")]
    [InlineData(".form-row {")]
    [InlineData(".form-actions {")]
    [InlineData(".option-card {")]
    [InlineData(".option-text {")]
    [InlineData(".button.danger {")]
    [InlineData(".empty-state {")]
    [InlineData(".note-error {")]
    public void Has_the_rule(string selector) => Assert.Contains(selector, Css, StringComparison.Ordinal);

    [Fact]
    public void Lets_the_content_grow_to_1760_pixels_from_1600_and_lines_the_header_up_with_it()
    {
        Assert.Matches(@"--content-max:\s*1440px", Css);
        Assert.Matches(@"@media \(min-width: 1600px\)\s*\{\s*:root\s*\{\s*--content-max:\s*1760px", Css);
        Assert.Matches(@"main\s*\{\s*max-width:\s*var\(--content-max\)", Css);
        Assert.Contains("(100% - var(--content-max)) / 2", Css, StringComparison.Ordinal);
    }

    [Fact]
    public void Gives_every_inline_icon_a_size_so_a_drawing_can_never_fill_the_page()
    {
        Assert.Matches(@"svg\.icon\s*\{\s*width:\s*20px;\s*height:\s*20px", Css);
        Assert.Matches(@"\.callout \.icon\s*\{\s*width:\s*20px;\s*height:\s*20px;\s*flex:\s*none", Css);
    }

    [Fact]
    public void Sizes_the_empty_state_icon_above_the_general_icon_rule_and_gives_the_four_notes_four_colours()
    {
        Assert.Matches(@"\.empty-state \.empty-icon\s*\{\s*width:\s*56px;\s*height:\s*56px", Css);
        var edges = new[] { "success", "warning", "info", "error" }
            .Select(v => Regex.Match(Css, @"\.note-" + v + @"\s*\{\s*border-left-color:\s*var\((--[a-z]+)\)").Groups[1].Value)
            .ToArray();
        Assert.Equal(4, edges.Distinct().Count());
        Assert.All(edges, e => Assert.NotEqual(string.Empty, e));
        Assert.Matches(@"\.note-info \.icon\s*\{\s*color:", Css);
    }

    [Fact]
    public void Keeps_text_to_about_72_characters_and_forms_to_720_pixels()
    {
        Assert.Matches(@"\.measure,[^{]*\{\s*max-width:\s*72ch", Css);
        Assert.Matches(@"\.form-col\s*\{\s*max-width:\s*720px", Css);
    }
}

/// <summary>Every destructive submit is the danger button (S-069).</summary>
public sealed class DangerButtonTests
{
    [Theory]
    [InlineData("Pages/PersonDelete.razor", "Delete_Confirm")]
    [InlineData("Pages/PersonMerge.razor", "Merge_Confirm")]
    [InlineData("Pages/Places.razor", "Places_DeleteNamed")]
    [InlineData("Pages/Settings.razor", "Settings_Faces_OffConfirm")]
    [InlineData("Pages/Settings.razor", "Settings_Faces_Off\"]")]
    [InlineData("Pages/ConnectedAccounts.razor", "Sources_Disconnect\"]")]
    public void Uses_the_danger_class_on_the_destructive_submit(string page, string text)
    {
        var markup = File.ReadAllText(Path.Combine(SourceTree.WebProject, "Components", page));
        var at = markup.IndexOf(text, StringComparison.Ordinal);
        Assert.True(at > 0, text);
        var button = markup[markup.LastIndexOf("<button", at, StringComparison.Ordinal)..at];
        Assert.Contains("button danger", button, StringComparison.Ordinal);
    }
}
