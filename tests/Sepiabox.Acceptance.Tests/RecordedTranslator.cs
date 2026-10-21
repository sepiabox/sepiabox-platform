using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Search.Questions;
using Sepiabox.Web.Services;

namespace Sepiabox.Acceptance.Tests;

/// <summary>
/// The real translator with an Anthropic API that answers with a recorded text, as the golden tests of S-025 and
/// S-026 replay theirs: the response still goes through the translator's own checks and the DSL's parser.
/// </summary>
public static class RecordedTranslator
{
    public static AnthropicQuestionTranslator Replaying(string response) => new(
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
