using System.Text.Json.Nodes;
using Sepiabox.Catalog.Search;
using Sepiabox.Search.Questions;

namespace Sepiabox.Acceptance.Tests;

/// <summary>The acceptance questions and their recorded responses, without Docker or the API (S-054).</summary>
public sealed class AcceptanceQuestionsTests
{
    private static readonly IReadOnlyList<AcceptanceQuestion> Questions = AcceptanceQuestions.Load();

    [Fact]
    public void Asks_the_story_s_counting_questions_in_english_and_portuguese()
    {
        Assert.Equal(Questions.Count, Questions.Select(q => q.Id).Distinct().Count());
        Assert.All(Questions, q =>
        {
            Assert.False(string.IsNullOrWhiteSpace(q.English));
            Assert.False(string.IsNullOrWhiteSpace(q.Portuguese));
            Assert.NotEqual(q.English, q.Portuguese);
        });
        // Photos per year, videos per year and items with no date, as the owner's check asks (docs/operations.md).
        Assert.Superset(
            new HashSet<string> { "photosByYear", "videosByYear", "undated", "photos1901", "videosInLisbon", "itemsByCity", "total" },
            Questions.Select(q => q.Expect).ToHashSet());
    }

    [Fact]
    public void Every_expected_query_is_a_valid_count()
    {
        Assert.All(Questions, q =>
        {
            Assert.True(SearchQueryJson.TryParse(q.Expected, out var query, out var error), $"{q.Id}: {error}");
            Assert.True(query.Aggregate is not null, $"{q.Id} is not a count.");
        });
    }

    [Fact]
    public void Every_expect_names_a_reference_when_the_report_has_numbers()
    {
        var report = JsonNode.Parse("""{"total":3,"years":[{"year":1901,"photos":1,"videos":0,"total":1}],"undated":{"photos":1,"videos":1,"total":2}}""")!;
        var generated = new FixtureExpectations([]);

        Assert.All(Questions, q => Assert.NotNull(ExpectedAnswer.For(q.Expect, report, generated)));
    }

    [Fact]
    public async Task Every_recorded_response_passes_the_translator_and_the_dsl_parser()
    {
        var recorded = AcceptanceQuestions.Recorded();
        var placeholders = AcceptanceQuestions.Placeholders();
        var context = QuestionContext.For(new DateOnly(2026, 10, 21), "Europe/Lisbon").Build();

        foreach (var question in Questions)
        {
            foreach (var (key, text) in question.InBothLanguages())
            {
                Assert.True(recorded.TryGetValue(key, out var response), $"No recorded response for {key}.");
                using var translator = RecordedTranslator.Replaying(response!);

                var result = await translator.TranslateAsync(text, context);

                Assert.True(result.Outcome == QuestionOutcome.Translated, $"{key}: {result.Outcome}");
                Assert.True(SearchQueryJson.TryParse(result.Json!, out _, out var error), $"{key}: {error}");
            }
        }

        Assert.Equal(Questions.Count * 2, recorded.Count);
        Assert.All(placeholders, key => Assert.Contains(key, recorded.Keys));
    }
}
