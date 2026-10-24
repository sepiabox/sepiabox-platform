using System.Text.RegularExpressions;

namespace Sepiabox.Web.Tests;

/// <summary>The merge page's stylesheet rules (#125): its own classes must not collide with the global ones.</summary>
public sealed class MergeStylesheetTests
{
    private static string Css { get; } = File.ReadAllText(Path.Combine(SourceTree.WebProject, "wwwroot", "app.css"));

    [Fact]
    public void The_merge_page_s_badge_is_not_the_absolutely_positioned_video_badge()
    {
        // A global .badge sits at the corner of a thumbnail; the merge page's own class must stay in the flow of its card.
        var rules = Regex.Matches(Css, @"\.merge-[\w-]*badge[^{]*\{([^}]*)\}", RegexOptions.None, TimeSpan.FromSeconds(5)).Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(rules);
        Assert.All(rules, rule => Assert.DoesNotContain("position: absolute", rule, StringComparison.Ordinal));
        Assert.DoesNotContain(".merge-people .badge", Css, StringComparison.Ordinal);
    }

    [Fact]
    public void The_merge_cards_stack_on_a_phone_and_the_arrow_points_down()
    {
        var phone = Regex.Match(Css, @"@media \(max-width: 600px\)\s*\{\s*\.merge-people\s*\{([^}]*)\}\s*\.merge-arrow\s*\{([^}]*)\}", RegexOptions.None, TimeSpan.FromSeconds(5));

        Assert.True(phone.Success);
        Assert.Contains("flex-direction: column", phone.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("rotate(90deg)", phone.Groups[2].Value, StringComparison.Ordinal);
    }
}
