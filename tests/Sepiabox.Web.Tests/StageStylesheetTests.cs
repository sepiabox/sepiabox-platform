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
    public void The_region_tools_on_the_dark_stage_are_light_enough_to_read_in_both_themes()
    {
        var rule = Regex.Match(Css, @"\.stage \.muted,\s*\.stage \.region-status\s*\{\s*color:\s*(#[0-9a-fA-F]{6})", RegexOptions.None, TimeSpan.FromSeconds(5));
        var stage = Regex.Match(Css, @"--stage:\s*(#[0-9a-fA-F]{6})", RegexOptions.None, TimeSpan.FromSeconds(5));

        Assert.True(rule.Success && stage.Success);
        var (a, b) = (Luminance(rule.Groups[1].Value), Luminance(stage.Groups[1].Value));
        Assert.True((Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05) >= 4.5);
    }

    [Fact]
    public void The_stage_has_a_hairline_in_the_dark_theme_and_is_shorter_on_a_phone()
    {
        Assert.Matches(@":root:not\(\[data-theme=""light""\]\) \.stage\s*\{[^}]*border:\s*1px solid var\(--border\)", Css);
        Assert.Matches(@":root\[data-theme=""dark""\] \.stage\s*\{[^}]*border:\s*1px solid var\(--border\)", Css);
        Assert.Matches(@"@media \(max-width: 600px\)\s*\{\s*\.preview\s*\{\s*min-height:\s*240px", Css);
    }

    private static double Luminance(string hex)
    {
        double Channel(int offset)
        {
            var c = int.Parse(hex.AsSpan(1 + offset, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(0)) + (0.7152 * Channel(2)) + (0.0722 * Channel(4));
    }

    [Fact]
    public void The_grid_is_three_columns_on_a_phone()
    {
        Assert.Matches(@"@media \(max-width: 600px\)\s*\{\s*\.grid\s*\{\s*grid-template-columns:\s*repeat\(3, 1fr\)", Css);
    }
}
