using Sepiabox.Search.Questions;

namespace Sepiabox.Web.Services;

/// <summary>
/// Keeps the API's compiled output format warm (issue 85, D-101). The API caches a compiled grammar for 24 hours from its
/// last use and the first call after that takes 50 to 55 s, so when the page with the question box is shown and no question
/// has been translated for <see cref="Quiet"/>, one tiny question is sent in the background with the real format. It is not
/// counted against anyone's daily limit, never blocks or fails the page, runs at most once at a time, and is not repeated
/// for <see cref="RetryAfter"/> after a failure. Only an outcome and token counts are logged, never a question.
/// </summary>
public sealed partial class QuestionWarmUp(IQuestionTranslator translator, TimeProvider clock, ILogger<QuestionWarmUp> log)
{
    /// <summary>How long after a translated question the format is still warm: half the API's 24 hours.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromHours(12);

    /// <summary>How long a failed warm-up keeps the next one away, so a page shown again and again does not retry at once.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

    /// <summary>The question sent: short, and one the format answers with a few tokens.</summary>
    public const string Question = "photos";

    /// <summary><c>Questions:WarmUp</c>, true unless configured otherwise (tests turn it off so a page never asks the fake translator).</summary>
    public bool Enabled { get; init; } = true;

    private readonly Lock _lock = new();
    private DateTimeOffset? _lastSuccess;
    private DateTimeOffset? _lastFailure;
    private Task? _running;

    /// <summary>The warm-up in progress, for tests to wait for; null when none.</summary>
    public Task? Running
    {
        get
        {
            lock (_lock)
            {
                return _running;
            }
        }
    }

    /// <summary>A question was translated: the format is warm for another <see cref="Quiet"/>.</summary>
    public void NoteSuccess()
    {
        lock (_lock)
        {
            _lastSuccess = clock.GetUtcNow();
        }
    }

    /// <summary>Starts a warm-up when one is due and returns at once; true when it started.</summary>
    public bool TryStart()
    {
        if (!Enabled || !translator.IsConfigured)
        {
            return false;
        }

        lock (_lock)
        {
            var now = clock.GetUtcNow();
            if (_running is not null
                || (_lastSuccess is { } success && now - success < Quiet)
                || (_lastFailure is { } failure && now - failure < RetryAfter))
            {
                return false;
            }

            _running = Task.Run(RunAsync);
            return true;
        }
    }

    private async Task RunAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var translation = await translator.TranslateAsync(Question, QuestionContext.For(today, "UTC").Build());
            lock (_lock)
            {
                if (translation.Outcome == QuestionOutcome.Translated)
                {
                    _lastSuccess = clock.GetUtcNow();
                }
                else
                {
                    _lastFailure = clock.GetUtcNow();
                }
            }

            LogDone(log, translation.Outcome, translation.Usage.InputTokens, translation.Usage.OutputTokens, translation.Usage.CacheReadInputTokens);
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _lastFailure = clock.GetUtcNow();
            }

            LogFailed(log, ex.GetType().Name);
        }
        finally
        {
            lock (_lock)
            {
                _running = null;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Question format warm-up ({Outcome}): {InputTokens} input, {OutputTokens} output, {CacheReadTokens} cached tokens.")]
    private static partial void LogDone(ILogger logger, QuestionOutcome outcome, long inputTokens, long outputTokens, long cacheReadTokens);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Question format warm-up failed: {ExceptionType}.")]
    private static partial void LogFailed(ILogger logger, string exceptionType);
}
