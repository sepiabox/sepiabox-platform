using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Search.Questions;
using Sepiabox.Web.Services;
using Xunit.Abstractions;

namespace Sepiabox.Acceptance.Tests;

/// <summary>
/// The MVP's acceptance with the real Anthropic API (S-054, D-054): every acceptance question, in both languages,
/// must be answered with <c>report</c>'s or the generator's numbers, which is how its meaning is compared (one retry
/// per question, recorded). Never in CI (Category=LiveApi); <c>SEPIABOX_RECORD_GOLDEN=1</c> also rewrites
/// <c>acceptance.recorded.json</c>, which <see cref="MvpAcceptanceTests"/> replays. The world is built inside the test,
/// not as a fixture, so a run without the key starts no container.
/// </summary>
[Trait("Category", "LiveApi")]
public sealed class MvpAcceptanceLiveTests(ITestOutputHelper output)
{
    [LiveApiFact]
    public async Task Answers_every_question_with_the_numbers_of_report_and_the_generator_with_the_real_model()
    {
        var key = Environment.GetEnvironmentVariable(LiveApiFactAttribute.KeyVariable)!;
        await PreflightAsync(key);
        var options = new AnthropicQuestionOptions { ApiKey = key, MaxRetries = 2, Timeout = TimeSpan.FromSeconds(60) };
        using var translator = new AnthropicQuestionTranslator(options, QuestionService.Validate, NullLogger<AnthropicQuestionTranslator>.Instance);
        using var world = new MvpWorld();
        await world.InitializeAsync();
        try
        {
            var responses = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var retried = new List<string>();
            var failed = new List<string>();
            var questions = AcceptanceQuestions.Load();
            foreach (var question in questions)
            {
                var expected = ExpectedAnswer.For(question.Expect, world.Report, world.Expected);
                foreach (var (id, text) in question.InBothLanguages())
                {
                    // One retry for a miss, recorded (S-025).
                    for (var attempt = 1; attempt <= 2; attempt++)
                    {
                        var recording = new RecordingTranslator(translator);
                        var misses = await MvpAnswers.CheckAsync(world, id, text, recording, expected);
                        responses[id] = recording.Json ?? "{}";
                        if (misses.Count == 0)
                        {
                            if (attempt == 2)
                            {
                                retried.Add(id);
                            }

                            break;
                        }

                        output.WriteLine($"{id} attempt {attempt}: {recording.Json}\n  {string.Join("\n  ", misses)}");
                        if (attempt == 2)
                        {
                            failed.AddRange(misses);
                        }
                    }
                }
            }

            var total = questions.Count * 2;
            var failedQuestions = failed.Select(f => f[..f.IndexOf(' ', StringComparison.Ordinal)]).Distinct().Count();
            output.WriteLine($"Model {options.Model}, {DateTime.UtcNow:yyyy-MM-dd}: {total - failedQuestions}/{total} answered right, {retried.Count} after a retry ({string.Join(", ", retried)}).");
            if (Environment.GetEnvironmentVariable("SEPIABOX_RECORD_GOLDEN") == "1")
            {
                Record(options.Model, responses);
            }

            Assert.True(failed.Count == 0, "Answers that differ:\n" + string.Join('\n', failed));
        }
        finally
        {
            await world.DisposeAsync();
        }
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
            $"Anthropic answered {(int)response.StatusCode}: {LiveApiFactAttribute.KeyVariable} is missing or wrong, so S-054 is blocked.");
        response.EnsureSuccessStatusCode();
    }

    private static void Record(string model, IDictionary<string, string> responses)
    {
        var path = AcceptanceQuestions.PathOf(AcceptanceQuestions.RecordedFileName);
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["description"] = "Responses replayed by S-054's acceptance test in CI: the real model's answers from the last live run (SEPIABOX_RECORD_GOLDEN=1), which rewrites this file.";
        root["source"] = "live";
        // Every response now comes from the model.
        root.Remove("placeholders");
        root["model"] = model;
        root["recordedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var recorded = new JsonObject();
        foreach (var (id, json) in responses)
        {
            recorded[id] = json;
        }

        root["responses"] = recorded;
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n");
    }

    // Keeps the model's text, which is what the recorded file replays.
    private sealed class RecordingTranslator(IQuestionTranslator inner) : IQuestionTranslator
    {
        public string? Json { get; private set; }

        public bool IsConfigured => inner.IsConfigured;

        public async Task<QuestionTranslation> TranslateAsync(string question, QuestionContext context, CancellationToken cancellationToken = default)
        {
            var result = await inner.TranslateAsync(question, context, cancellationToken);
            Json = result.Json;
            return result;
        }
    }
}

/// <summary>
/// A test that calls the real Anthropic API: skipped without <c>SEPIABOX_ANTHROPIC_API_KEY</c>; CI never runs
/// Category=LiveApi. The key is only ever sent to the API, never written anywhere.
/// </summary>
internal sealed class LiveApiFactAttribute : FactAttribute
{
    // Not ANTHROPIC_API_KEY: Claude Code reserves that name for its own sign-in and keeps it out of the agents' sessions.
    public const string KeyVariable = "SEPIABOX_ANTHROPIC_API_KEY";

    public LiveApiFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeyVariable)))
        {
            Skip = $"{KeyVariable} is not set; the live acceptance run is required before S-054 is done (D-054).";
        }
    }
}
