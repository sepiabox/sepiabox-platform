using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog.Search;
using Sepiabox.Search.Questions;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The golden set (S-025) without the API: the file itself, and the recorded responses replayed through the real
/// translator and the DSL's parser. The live run against Anthropic is <see cref="GoldenQuestionsLiveTests"/>.
/// </summary>
public sealed class GoldenQuestionsTests
{
    private static readonly GoldenQuestions Golden = GoldenQuestions.Load();

    [Fact]
    public void Has_at_least_30_questions_each_in_english_and_portuguese_covering_what_the_dsl_has()
    {
        Assert.True(Golden.Questions.Count >= 30, $"{Golden.Questions.Count} questions.");
        Assert.All(Golden.Questions, q =>
        {
            Assert.False(string.IsNullOrWhiteSpace(q.English));
            Assert.False(string.IsNullOrWhiteSpace(q.Portuguese));
            Assert.NotEqual(q.English, q.Portuguese);
        });
        Assert.Equal(Golden.Questions.Count, Golden.Questions.Select(q => q.Id).Distinct().Count());
        var covered = Golden.Questions.SelectMany(q => q.Covers).ToHashSet();
        Assert.Superset(
            new HashSet<string> { "dates", "range", "on this day", "places", "Lisboa", "countries", "kinds", "sources", "descriptions", "undated", "people", "me" },
            covered);
        // At least 10 counting questions (S-026), by year, kind, place, source and "on this day".
        var counting = Golden.Questions.Where(q => q.Expected.Contains("\"aggregate\"", StringComparison.Ordinal)).ToList();
        Assert.True(counting.Count >= 10, $"{counting.Count} counting questions.");
        Assert.Superset(
            new HashSet<string> { "by year", "by kind", "places", "sources", "on this day" },
            counting.SelectMany(q => q.Covers).ToHashSet());
        // "Lisboa" is asked for as Lisbon, the name GeoNames stores.
        Assert.Contains(Golden.Questions, q => q.Portuguese.Contains("Lisboa", StringComparison.Ordinal) && q.Expected.Contains("\"Lisbon\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_expected_query_is_valid_dsl()
    {
        Assert.All(Golden.Questions, q => Assert.True(SearchQueryJson.TryParse(q.Expected, out _, out var error), $"{q.Id}: {error}"));
    }

    [Fact]
    public async Task Every_recorded_response_passes_the_translator_and_the_dsl_parser()
    {
        var recorded = Recorded();
        var context = Golden.Context();

        foreach (var question in Golden.Questions)
        {
            foreach (var (key, text) in question.InBothLanguages())
            {
                Assert.True(recorded.TryGetValue(key, out var response), $"No recorded response for {key}.");
                using var translator = Replaying(response!);

                var result = await translator.TranslateAsync(text, context);

                Assert.True(result.Outcome == QuestionOutcome.Translated, $"{key}: {result.Outcome}");
                Assert.True(SearchQueryJson.TryParse(result.Json!, out _, out var error), $"{key}: {error}");
            }
        }
    }

    [Fact]
    public void Context_has_the_golden_day_and_the_catalog_s_sources_cities_and_countries()
    {
        var context = Golden.Context();

        Assert.Equal(new DateOnly(2026, 10, 20), context.Today);
        Assert.Equal(["Celular do Bruno", "HD da Ana"], context.Lists.Single(l => l.DslField == "filters.sources").Values);
        Assert.Contains("Lisbon", context.Lists.Single(l => l.DslField == "filters.places.cities").Values);
        Assert.Equal(["BR", "ES", "PT"], context.Lists.Single(l => l.DslField == "filters.places.countries").Values);
        // People's names, never an address (D-080).
        Assert.Equal(["Ana", "Bruno", "Carla", "Pedro"], context.Lists.Single(l => l.DslField == "filters.people.all").Values);
        Assert.DoesNotContain(context.Lists.SelectMany(l => l.Values), v => v.Contains('@', StringComparison.Ordinal));
    }

    /// <summary>The recorded responses, by "id/en" and "id/pt".</summary>
    internal static Dictionary<string, string> Recorded()
    {
        var root = JsonNode.Parse(File.ReadAllText(GoldenQuestions.PathOf(GoldenQuestions.RecordedFileName)))!;
        return root["responses"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<string>());
    }

    // The real translator, with an API that answers with the recorded text.
    private static AnthropicQuestionTranslator Replaying(string response) => new(
        new AnthropicQuestionOptions { ApiKey = "recorded", MaxRetries = 0 },
        QuestionService.Validate,
        NullLogger<AnthropicQuestionTranslator>.Instance,
        new HttpClient(new Replay(response)));

    private sealed class Replay(string text) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = new JsonObject
            {
                ["id"] = "msg_recorded",
                ["type"] = "message",
                ["role"] = "assistant",
                ["model"] = AnthropicQuestionOptions.DefaultModel,
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                ["stop_reason"] = "end_turn",
                ["stop_sequence"] = null,
                ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 1 },
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            });
        }
    }
}

/// <summary>
/// The recorded responses mean what the golden queries mean: run on the fixture catalog, they find the same assets
/// (S-025). In CI's integration job, with PostgreSQL.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class GoldenQuestionsCatalogTests(CatalogFixture catalog)
{
    [Fact]
    public async Task Every_recorded_response_finds_what_its_expected_query_finds()
    {
        var golden = GoldenQuestions.Load();
        var recorded = GoldenQuestionsTests.Recorded();
        await using var db = await golden.SeedAsync(catalog);

        var misses = new List<string>();
        foreach (var question in golden.Questions)
        {
            var expected = await golden.FindAsync(db, question.Expected);
            foreach (var (key, _) in question.InBothLanguages())
            {
                if (!(await golden.FindAsync(db, recorded[key])).SequenceEqual(expected))
                {
                    misses.Add(key);
                }
            }
        }

        Assert.Empty(misses);
    }

    [Fact]
    public async Task The_fixture_catalog_tells_the_golden_queries_apart()
    {
        var golden = GoldenQuestions.Load();
        await using var db = await golden.SeedAsync(catalog);

        var results = new Dictionary<string, List<string>>();
        foreach (var question in golden.Questions)
        {
            results[question.Id] = await golden.FindAsync(db, question.Expected);
        }

        // Every question finds something (but the injection, which finds the whole library), and no two find the same.
        Assert.All(results, r => Assert.NotEmpty(r.Value));
        Assert.Equal(results.Count, results.Values.Select(r => string.Join(',', r)).Distinct().Count());
    }
}
