using System.Text.RegularExpressions;

namespace Sepiabox.Web.Tests;

/// <summary>The detail page's stage and the grid's tiles in the stylesheet (S-068): keyboard focus shows on the dark stage, and a finger gets no hover overlay.</summary>
public sealed partial class StageStylesheetTests
{
    private static string Css { get; } = File.ReadAllText(Path.Combine(SourceTree.WebProject, "wwwroot", "app.css"));

    [Fact]
    public void The_arrows_on_the_dark_stage_have_a_light_focus_ring_since_the_pages_accent_would_not_show_on_it()
    {
        var rule = Regex.Match(Css, @"\.nav-arrow:focus-visible\s*\{([^}]*)\}", RegexOptions.None, TimeSpan.FromSeconds(5));

        Assert.True(rule.Success);
        Assert.Contains("outline: 3px solid #fff", rule.Groups[1].Value, StringComparison.Ordinal);
        Assert.Matches(@"\.preview\s*\{[^}]*background:\s*var\(--stage\)", Css);
    }

    [Fact]
    public void A_tile_shows_its_overlay_on_keyboard_focus_as_on_hover_and_not_at_all_where_there_is_no_hover()
    {
        Assert.Matches(@"\.thumb:focus-visible::after[^{]*\{[^}]*opacity:\s*1", Css);
        Assert.Matches(@"@media \(hover: none\)\s*\{[^}]*\.thumb::after", Css);
    }

    [Fact]
    public void The_grid_is_three_columns_on_a_phone()
    {
        Assert.Matches(@"@media \(max-width: 600px\)\s*\{\s*\.grid\s*\{\s*grid-template-columns:\s*repeat\(3, 1fr\)", Css);
    }
}
