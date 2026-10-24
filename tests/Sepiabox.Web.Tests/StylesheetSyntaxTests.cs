using System.Text.RegularExpressions;

namespace Sepiabox.Web.Tests;

/// <summary>
/// A missing brace in app.css nests every later rule inside the unclosed one, so they silently stop matching; no other test
/// sees it, because each checks a rule's text, not whether the browser can reach it.
/// </summary>
public sealed partial class StylesheetSyntaxTests
{
    [Fact]
    public void Closes_every_block_it_opens()
    {
        var css = CommentPattern().Replace(File.ReadAllText(Path.Combine(SourceTree.WebProject, "wwwroot", "app.css")), " ");
        var depth = 0;
        var lastBalancedLine = 0;
        var lines = css.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            depth += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');
            Assert.True(depth >= 0, $"app.css closes a block it never opened on line {i + 1}.");
            if (depth == 0)
            {
                lastBalancedLine = i + 1;
            }
        }

        Assert.True(depth == 0, $"app.css leaves a block open after line {lastBalancedLine}.");
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentPattern();
}
