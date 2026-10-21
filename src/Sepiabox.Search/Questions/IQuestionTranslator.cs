namespace Sepiabox.Search.Questions;

/// <summary>
/// Turns a question typed in English or Portuguese into the search DSL v1 (S-025, D-020). The project's own interface
/// (D-051): the web and the end-to-end test depend on it, and only its Anthropic implementation knows the provider.
/// </summary>
public interface IQuestionTranslator
{
    /// <summary>False when no API key is configured: the question box then says questions are off (S-025).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// The DSL for <paramref name="question"/>. Never throws for what the provider answers or fails to answer: the
    /// outcome says what happened. Only <see cref="QuestionTranslation.Json"/> of a <see cref="QuestionOutcome.Translated"/>
    /// result is meant to be used, and it has already passed the validator the implementation was given.
    /// </summary>
    Task<QuestionTranslation> TranslateAsync(string question, QuestionContext context, CancellationToken cancellationToken = default);
}

public enum QuestionOutcome
{
    /// <summary>A DSL query that passed validation.</summary>
    Translated,

    /// <summary>The model refused, or answered with something that is not a valid query, even after a retry.</summary>
    NotUnderstood,

    /// <summary>The provider is busy, down, slow or unreachable: worth trying again later.</summary>
    Unavailable,

    /// <summary>The provider refused the credentials or the request itself: an operator has to look.</summary>
    Failed,

    /// <summary>No API key is configured.</summary>
    NotConfigured,
}

/// <summary>Tokens the provider billed for one question, over every attempt.</summary>
public sealed record TokenUsage(long InputTokens, long OutputTokens, long CacheReadInputTokens, long CacheCreationInputTokens)
{
    public static readonly TokenUsage None = new(0, 0, 0, 0);

    public TokenUsage Add(TokenUsage other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(
            InputTokens + other.InputTokens,
            OutputTokens + other.OutputTokens,
            CacheReadInputTokens + other.CacheReadInputTokens,
            CacheCreationInputTokens + other.CacheCreationInputTokens);
    }
}

/// <param name="Outcome">What happened.</param>
/// <param name="Json">The DSL query's JSON when <paramref name="Outcome"/> is <see cref="QuestionOutcome.Translated"/>.</param>
/// <param name="Usage">Tokens billed, whatever the outcome.</param>
/// <param name="Attempts">Requests answered by the model (a second one follows an invalid first answer).</param>
public sealed record QuestionTranslation(QuestionOutcome Outcome, string? Json, TokenUsage Usage, int Attempts)
{
    public static QuestionTranslation Of(QuestionOutcome outcome, TokenUsage usage, int attempts) => new(outcome, null, usage, attempts);
}

/// <summary>Checks a candidate DSL query: null when it is valid, otherwise what is wrong (sent back to the model once).</summary>
public delegate string? QueryValidator(string json);
