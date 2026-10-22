using System.Text.RegularExpressions;
using Sepiabox.Search.Questions;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The translator prompt's examples (#114, D-113) must not be the golden questions: the live golden run then measures whether
/// the model understood the rules, not whether it copied an example it was shown with its answer.
/// </summary>
public sealed partial class PromptExamplesTests
{
    // An example is a quoted question followed by " -> " and its answer.
    [GeneratedRegex("^\\s*\"(?<question>[^\"\\n]+)\" -> ", RegexOptions.Multiline)]
    private static partial Regex Example();

    [Fact]
    public void Shows_no_example_that_is_a_golden_question_in_either_language_ignoring_case_and_punctuation()
    {
        var golden = GoldenQuestions.Load().Questions
            .SelectMany(q => new[] { (q.Id, Question: q.English), (q.Id, Question: q.Portuguese) })
            .ToDictionary(q => Normalize(q.Question), q => q.Id);
        var examples = Example().Matches(QuestionPrompt.System).Select(m => m.Groups["question"].Value).ToList();

        Assert.True(examples.Count > 40, $"found only {examples.Count} examples: the pattern no longer matches the prompt");
        var copies = examples.Where(e => golden.ContainsKey(Normalize(e))).Select(e => $"{e} ({golden[Normalize(e)]})").ToList();
        Assert.Empty(copies);
    }

    [Theory]
    [InlineData("Photos from Porto, last summer!", "photos from porto last summer")]
    [InlineData("  how many   weddings did we go to in 2026?", "how many weddings did we go to in 2026")]
    [InlineData("Vídeos de Lisboa", "vídeos de lisboa")]
    public void Compares_by_words_not_by_case_punctuation_or_spacing(string text, string expected) =>
        Assert.Equal(expected, Normalize(text));

    private static string Normalize(string text) =>
        Whitespace().Replace(Punctuation().Replace(text.ToLowerInvariant(), string.Empty), " ").Trim();

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
