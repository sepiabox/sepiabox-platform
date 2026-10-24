using System.Text.RegularExpressions;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The header's stylesheet rules (S-066, D-120): it sticks only where it is one row, since a sticky header of two or three rows would cover a
/// quarter of a phone's screen, and the sticky header's height is only counted for anchored targets where it sticks.
/// </summary>
public sealed partial class HeaderStylesheetTests
{
    private static string Css { get; } = File.ReadAllText(Path.Combine(SourceTree.WebProject, "wwwroot", "app.css"));

    [Fact]
    public void The_header_is_sticky_only_inside_a_media_query_for_wide_screens_and_stacked_above_the_page_everywhere_else()
    {
        var topRules = TopRule().Matches(Css).Select(m => (m.Index, Body: m.Groups[1].Value)).ToList();
        var wide = WideBlock().Match(Css);
        Assert.True(wide.Success, "no (min-width: 960px) block that sets .top");

        // The rule that sticks is inside the wide block; the plain .top rule is relative with a z-index, so the "More" popover paints
        // above positioned page content where the header is not sticky.
        var sticky = topRules.Where(r => r.Body.Contains("position: sticky", StringComparison.Ordinal)).ToList();
        Assert.Single(sticky);
        Assert.InRange(sticky[0].Index, wide.Index, wide.Index + wide.Length);
        Assert.Contains(topRules, r => r.Body.Contains("position: relative", StringComparison.Ordinal) && r.Body.Contains("z-index", StringComparison.Ordinal) && !(r.Index >= wide.Index && r.Index <= wide.Index + wide.Length));
    }

    [Fact]
    public void The_height_anchored_targets_leave_for_the_header_is_nothing_except_where_it_sticks()
    {
        Assert.Matches(@":root\s*\{[^}]*--header-h:\s*0px", Css);
        var wide = Regex.Match(Css, @"@media \(min-width: 960px\)\s*\{\s*:root\s*\{\s*--header-h:\s*(\d+)px", RegexOptions.None, TimeSpan.FromSeconds(5));
        Assert.True(wide.Success);
        Assert.InRange(int.Parse(wide.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 60, 70);
        Assert.DoesNotContain("--header-h: 112px", Css, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\n\s*\.top\s*\{([^}]*)\}", RegexOptions.None, 5000)]
    private static partial Regex TopRule();

    [GeneratedRegex(@"@media \(min-width: 960px\)\s*\{\s*\.top\s*\{[^}]*\}\s*\}", RegexOptions.None, 5000)]
    private static partial Regex WideBlock();
}
