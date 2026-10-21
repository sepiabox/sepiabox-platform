using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;

namespace Sepiabox.Search.Questions;

/// <summary>Configuration section <c>Anthropic</c> (D-052): the key from user secrets locally and Key Vault in Azure.</summary>
public sealed record AnthropicQuestionOptions
{
    public const string Section = "Anthropic";

    /// <summary>Claude Haiku 5.5 (D-052); configurable so a model change is a setting, not a release.</summary>
    public const string DefaultModel = "claude-haiku-5-5";

    /// <summary>The standard Anthropic API, set explicitly so no <c>ANTHROPIC_BASE_URL</c> of the host redirects it.</summary>
    public const string DefaultBaseUrl = "https://api.anthropic.com";

    /// <summary><c>Anthropic:ApiKey</c>; blank means questions are off.</summary>
    public string? ApiKey { get; init; }

    public string Model { get; init; } = DefaultModel;

    public string BaseUrl { get; init; } = DefaultBaseUrl;

    /// <summary>A DSL query is a few hundred tokens; the rest leaves room for the model's reasoning at low effort.</summary>
    public int MaxTokens { get; init; } = 1024;

    /// <summary>Per request; a question is answered in seconds, and someone is waiting for it.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The SDK's own retries of 408, 409, 429, 5xx and connection errors, per request.</summary>
    public int MaxRetries { get; init; } = 1;

    /// <summary>Requests per question: the second one, after an invalid answer, says what was wrong.</summary>
    public int MaxAttempts { get; init; } = 2;
}

/// <summary>
/// <see cref="IQuestionTranslator"/> with Claude through the Anthropic API and its official C# SDK (D-052, D-051): the
/// only class that references the SDK. Structured outputs restrict the answer to the DSL schema, and every answer is
/// still checked by the <see cref="QueryValidator"/> it is given (<c>SearchQueryJson.TryParse</c> in the web) before
/// it is returned. Only the question and the context leave: no image, no embedding, no user identity (D-073).
/// </summary>
public sealed partial class AnthropicQuestionTranslator : IQuestionTranslator, IDisposable
{
    private readonly AnthropicQuestionOptions _options;
    private readonly QueryValidator _validate;
    private readonly ILogger<AnthropicQuestionTranslator> _log;
    private readonly AnthropicClient? _client;

    /// <param name="options">The key, model and limits.</param>
    /// <param name="validate">The DSL's own validation; the answer is never trusted without it.</param>
    /// <param name="log">Outcomes and token counts; never the question, the key or a request header.</param>
    /// <param name="httpClient">For tests: a client whose handler answers instead of the API.</param>
    public AnthropicQuestionTranslator(
        AnthropicQuestionOptions options, QueryValidator validate, ILogger<AnthropicQuestionTranslator> log, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validate);
        ArgumentNullException.ThrowIfNull(log);
        _options = options;
        _validate = validate;
        _log = log;
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            _client = httpClient is null
                ? new AnthropicClient
                {
                    ApiKey = options.ApiKey.Trim(),
                    // Explicit, so a token in the host's ANTHROPIC_AUTH_TOKEN is never sent instead.
                    AuthToken = null,
                    BaseUrl = options.BaseUrl,
                    Timeout = options.Timeout,
                    MaxRetries = options.MaxRetries,
                }
                : new AnthropicClient
                {
                    ApiKey = options.ApiKey.Trim(),
                    AuthToken = null,
                    BaseUrl = options.BaseUrl,
                    Timeout = options.Timeout,
                    MaxRetries = options.MaxRetries,
                    HttpClient = httpClient,
                };
        }
    }

    public bool IsConfigured => _client is not null;

    public void Dispose() => _client?.Dispose();

    /// <summary>
    /// The request for a question: the model, the DSL schema as the output format, low effort, the cached system
    /// prompt, and text only. <paramref name="previous"/> holds an earlier invalid answer and what was wrong with it.
    /// </summary>
    public MessageCreateParams BuildRequest(string question, QuestionContext context, (string Answer, string Problem)? previous = null)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(context);
        List<MessageParam> messages = [new() { Role = Role.User, Content = QuestionPrompt.UserMessage(question, context) }];
        if (previous is { } earlier)
        {
            // Not a prefill: the conversation ends with the user's correction, and the model answers anew.
            messages.Add(new() { Role = Role.Assistant, Content = earlier.Answer });
            messages.Add(new() { Role = Role.User, Content = QuestionPrompt.Correction(earlier.Problem) });
        }

        return new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = new List<TextBlockParam> { new() { Text = QuestionPrompt.System, CacheControl = new CacheControlEphemeral() } },
            Messages = messages,
            OutputConfig = new OutputConfig
            {
                Effort = Effort.Low,
                Format = new JsonOutputFormat { Schema = Schema() },
            },
        };
    }

    public async Task<QuestionTranslation> TranslateAsync(string question, QuestionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_client is null)
        {
            return QuestionTranslation.Of(QuestionOutcome.NotConfigured, TokenUsage.None, 0);
        }

        var cleaned = QuestionPrompt.Clean(question);
        if (cleaned is null)
        {
            return QuestionTranslation.Of(QuestionOutcome.NotUnderstood, TokenUsage.None, 0);
        }

        var usage = TokenUsage.None;
        (string Answer, string Problem)? previous = null;
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            Message reply;
            try
            {
                reply = await _client.Messages.Create(BuildRequest(cleaned, context, previous), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (Classify(ex) is { } outcome)
            {
                // The exception's type and status only: its message may quote the response, never the request.
                LogFailed(_log, outcome, ex.GetType().Name, attempt);
                return QuestionTranslation.Of(outcome, usage, attempt - 1);
            }

            usage = usage.Add(Usage(reply));
            var stopReason = reply.StopReason?.Raw();
            if (stopReason == "refusal")
            {
                LogRefused(_log, attempt);
                return QuestionTranslation.Of(QuestionOutcome.NotUnderstood, usage, attempt);
            }

            var answer = Text(reply);
            var problem = stopReason == "max_tokens"
                ? "The answer was cut off before it ended."
                : answer is null ? "The answer had no text." : _validate(answer);
            if (problem is null)
            {
                LogTranslated(_log, attempt, usage.InputTokens, usage.OutputTokens, usage.CacheReadInputTokens);
                return new QuestionTranslation(QuestionOutcome.Translated, answer, usage, attempt);
            }

            LogInvalid(_log, attempt);
            previous = (answer ?? "{}", problem);
        }

        return QuestionTranslation.Of(QuestionOutcome.NotUnderstood, usage, _options.MaxAttempts);
    }

    private static Dictionary<string, JsonElement> Schema()
    {
        using var schema = JsonDocument.Parse(QuestionPrompt.OutputSchemaJson);
        return schema.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    // The JSON answer is the text block; thinking blocks, if any, are not part of it.
    private static string? Text(Message reply)
    {
        var texts = new List<string>();
        foreach (var block in reply.Content)
        {
            if (block.TryPickText(out var text))
            {
                texts.Add(text.Text);
            }
        }

        var joined = string.Concat(texts).Trim();
        return joined.Length == 0 ? null : joined;
    }

    private static TokenUsage Usage(Message reply) =>
        reply.Usage is { } usage
            ? new TokenUsage(usage.InputTokens, usage.OutputTokens, usage.CacheReadInputTokens ?? 0, usage.CacheCreationInputTokens ?? 0)
            : TokenUsage.None;

    // Busy, down, slow or unreachable: try later. Refused credentials or request: an operator has to look.
    private static QuestionOutcome? Classify(Exception exception) => exception switch
    {
        AnthropicRateLimitException or Anthropic5xxException or AnthropicIOException => QuestionOutcome.Unavailable,
        AnthropicUnauthorizedException or AnthropicForbiddenException or AnthropicApiException => QuestionOutcome.Failed,
        AnthropicException => QuestionOutcome.Failed,
        HttpRequestException => QuestionOutcome.Unavailable,
        // The SDK's own timeout, not the caller's cancellation (that one is rethrown above).
        OperationCanceledException => QuestionOutcome.Unavailable,
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Question translated on attempt {Attempt}: {InputTokens} input tokens ({CacheReadTokens} from cache), {OutputTokens} output tokens.")]
    private static partial void LogTranslated(ILogger logger, int attempt, long inputTokens, long outputTokens, long cacheReadTokens);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model's answer on attempt {Attempt} was not a valid search query.")]
    private static partial void LogInvalid(ILogger logger, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The model refused the question on attempt {Attempt}.")]
    private static partial void LogRefused(ILogger logger, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "The question could not be translated ({Outcome}): {ExceptionType} on attempt {Attempt}.")]
    private static partial void LogFailed(ILogger logger, QuestionOutcome outcome, string exceptionType, int attempt);
}
