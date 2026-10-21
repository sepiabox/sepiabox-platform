using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Search.Questions;
using Sepiabox.Tests.Live;
using Sepiabox.Web.Services;
using Xunit.Abstractions;

namespace Sepiabox.Web.Tests;

/// <summary>
/// The golden set against the real Anthropic API (S-025, D-054): every question, in both languages, must mean its
/// expected query, which is checked by running both on the fixture catalog. Never in CI (Category=LiveApi); run
/// with <c>SEPIABOX_ANTHROPIC_API_KEY</c> before the story is done. <c>SEPIABOX_RECORD_GOLDEN=1</c> also refreshes the
/// recorded responses CI replays, only when every question passed (<see cref="LiveRecording"/>).
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "LiveApi")]
public sealed class GoldenQuestionsLiveTests(CatalogFixture catalog, ITestOutputHelper output)
{
    [LiveApiFact]
    public async Task Every_golden_question_means_its_expected_query_with_the_real_model()
    {
        var key = Environment.GetEnvironmentVariable(LiveApiFactAttribute.KeyVariable)!;
        await PreflightAsync(key);
        var golden = GoldenQuestions.Load();
        var options = new AnthropicQuestionOptions { ApiKey = key, MaxRetries = 2, Timeout = TimeSpan.FromSeconds(60) };
        using var translator = new AnthropicQuestionTranslator(options, QuestionService.Validate, NullLogger<AnthropicQuestionTranslator>.Instance);
        await using var db = await golden.SeedAsync(catalog);
        var context = golden.Context();

        var responses = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var retried = new List<string>();
        var failed = new List<string>();
        foreach (var question in golden.Questions)
        {
            var expected = await golden.FindAsync(db, question.Expected);
            foreach (var (id, text) in question.InBothLanguages())
            {
                // One retry for a miss, recorded (S-025).
                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    var result = await translator.TranslateAsync(text, context);
                    var found = result.Outcome == QuestionOutcome.Translated ? await golden.FindAsync(db, result.Json!) : null;
                    if (found is not null && found.SequenceEqual(expected))
                    {
                        responses[id] = result.Json!;
                        if (attempt == 2)
                        {
                            retried.Add(id);
                        }

                        break;
                    }

                    output.WriteLine($"{id} attempt {attempt}: {result.Outcome} {result.Json}");
                    if (attempt == 2)
                    {
                        failed.Add(id);
                        responses[id] = result.Json ?? "{}";
                    }
                }
            }
        }

        var total = golden.Questions.Count * 2;
        output.WriteLine($"Model {options.Model}, {DateTime.UtcNow:yyyy-MM-dd}: {total - failed.Count}/{total} passed, {retried.Count} after a retry ({string.Join(", ", retried)}).");
        output.WriteLine(LiveRecording.RecordIfPassed(
            LiveRecording.Requested, failed.Count, GoldenQuestions.PathOf(GoldenQuestions.RecordedFileName), () => Record(options.Model, responses)));

        Assert.Empty(failed);
    }

    // A 401 or 403 means the key is missing or wrong: the story is blocked, never a silent pass (S-025).
    private static async Task PreflightAsync(string key)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(AnthropicQuestionOptions.DefaultBaseUrl + "/v1/models?limit=1"));
        request.Headers.Add("x-api-key", key);
        request.Headers.Add("anthropic-version", "2023-06-01");
        using var response = await http.SendAsync(request);
        Assert.False(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Anthropic answered {(int)response.StatusCode}: {LiveApiFactAttribute.KeyVariable} is missing or wrong, so S-025 is blocked.");
        response.EnsureSuccessStatusCode();
    }

    private static void Record(string model, IDictionary<string, string> responses)
    {
        var path = GoldenQuestions.PathOf(GoldenQuestions.RecordedFileName);
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["description"] = "Responses replayed by the golden test in CI (S-025): the real model's answers from the last live run (SEPIABOX_RECORD_GOLDEN=1), which rewrites this file.";
        root["source"] = "live";
        // Every response now comes from the model.
        root.Remove("placeholders");
        root["model"] = model;
        root["recordedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var recorded = new JsonObject();
        foreach (var (id, json) in responses)
        {
            recorded[id] = json;
        }

        root["responses"] = recorded;
        File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n");
    }
}

/// <summary>
/// A test that calls the real Anthropic API: skipped without <c>SEPIABOX_ANTHROPIC_API_KEY</c> (the agents' environment has
/// it, CI never runs Category=LiveApi). The key is only ever sent to the API, never written anywhere.
/// </summary>
internal sealed class LiveApiFactAttribute : FactAttribute
{
    // Not ANTHROPIC_API_KEY: Claude Code reserves that name for its own sign-in and keeps it out of the agents' sessions.
    public const string KeyVariable = "SEPIABOX_ANTHROPIC_API_KEY";

    public LiveApiFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeyVariable)))
        {
            Skip = $"{KeyVariable} is not set; the live golden run is required before S-025 is done (D-054).";
        }
    }
}
