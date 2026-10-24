using System.Globalization;
using System.Text.RegularExpressions;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The design tokens of <c>wwwroot/app.css</c> (S-066, D-120) against WCAG: every text pair at 4.5:1 or more and every control border at 3:1
/// or more, in the light and in the dark palette. The values are read from the stylesheet, so a change of a token is checked.
/// </summary>
public sealed partial class TokenContrastTests
{
    // Text on the surfaces it is put on (foreground, background).
    private static readonly (string Foreground, string Background)[] s_textPairs =
    [
        ("text", "bg"), ("text", "surface"), ("text", "surface-2"),
        ("text-2", "bg"), ("text-2", "surface"), ("text-2", "surface-2"),
        ("text-3", "bg"), ("text-3", "surface"), ("text-3", "surface-2"),
        ("accent", "bg"), ("accent", "surface"), ("accent", "accent-soft"), ("accent-hover", "surface"), ("accent-hover", "accent-soft"),
        ("accent-contrast", "accent"), ("accent-contrast", "accent-hover"),
        ("brand-ink", "brand-soft"), ("brand", "surface"),
        ("danger", "surface"), ("danger", "bg"),
    ];

    // Control borders against what they sit on: a control must be told from its surroundings (WCAG 1.4.11).
    private static readonly (string Foreground, string Background)[] s_controlPairs =
    [
        ("control-border", "bg"), ("control-border", "surface"),
    ];

    public static TheoryData<string, string, string> TextPairs => Expand(s_textPairs);

    public static TheoryData<string, string, string> ControlPairs => Expand(s_controlPairs);

    [Theory]
    [MemberData(nameof(TextPairs))]
    public void Every_text_pair_reaches_4_5_to_1(string theme, string foreground, string background) =>
        Assert.True(Ratio(theme, foreground, background) >= 4.5, $"{theme}: {foreground} on {background} is {Ratio(theme, foreground, background):0.00}:1");

    [Theory]
    [MemberData(nameof(ControlPairs))]
    public void Every_control_border_reaches_3_to_1(string theme, string foreground, string background) =>
        Assert.True(Ratio(theme, foreground, background) >= 3.0, $"{theme}: {foreground} on {background} is {Ratio(theme, foreground, background):0.00}:1");

    [Fact]
    public void The_dark_palette_defines_every_token_the_light_one_does_so_none_is_left_light_by_mistake()
    {
        var light = Tokens("light");
        var dark = Tokens("dark");

        foreach (var name in light.Keys.Where(k => !k.StartsWith("shadow", StringComparison.Ordinal) && k != "stage" && k != "focus"))
        {
            Assert.True(dark.ContainsKey(name), $"--{name} has no dark value");
        }
    }

    [Fact]
    public void The_two_dark_blocks_say_the_same()
    {
        // One follows the system setting, the other a data-theme attribute; a copy that drifts would show two dark themes.
        Assert.Equal(Block(MediaDark()), Block(ExplicitDark()));
    }

    private static TheoryData<string, string, string> Expand((string Foreground, string Background)[] pairs)
    {
        var data = new TheoryData<string, string, string>();
        foreach (var theme in new[] { "light", "dark" })
        {
            foreach (var (foreground, background) in pairs)
            {
                data.Add(theme, foreground, background);
            }
        }

        return data;
    }

    private static double Ratio(string theme, string foreground, string background)
    {
        var tokens = Tokens(theme);
        var a = Luminance(tokens[foreground]);
        var b = Luminance(tokens[background]);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int offset)
        {
            var c = int.Parse(hex.AsSpan(1 + offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(0)) + (0.7152 * Channel(2)) + (0.0722 * Channel(4));
    }

    private static Dictionary<string, string> Tokens(string theme)
    {
        var tokens = Block(RootLight());
        if (theme == "dark")
        {
            foreach (var (name, value) in Block(ExplicitDark()))
            {
                tokens[name] = value;
            }
        }

        return tokens;
    }

    private static string Css { get; } = File.ReadAllText(Path.Combine(SourceTree.WebProject, "wwwroot", "app.css"));

    private static string RootLight() => BlockAfter(":root {");

    private static string ExplicitDark() => BlockAfter(":root[data-theme=\"dark\"] {");

    private static string MediaDark() => BlockAfter(":root:not([data-theme=\"light\"]) {");

    private static string BlockAfter(string selector)
    {
        var start = Css.IndexOf(selector, StringComparison.Ordinal);
        Assert.True(start >= 0, selector + " not found in app.css");
        var open = start + selector.Length;
        return Css[open..Css.IndexOf('}', open)];
    }

    // Only literal colours: a token that is an alias (var(--x)) or not a colour is left out.
    private static Dictionary<string, string> Block(string body) =>
        TokenLine().Matches(body).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);

    [GeneratedRegex(@"--([a-z0-9-]+):\s*(#[0-9a-fA-F]{6})\s*;")]
    private static partial Regex TokenLine();
}
