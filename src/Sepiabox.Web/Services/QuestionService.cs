using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;
using Sepiabox.Search.Questions;

namespace Sepiabox.Web.Services;

/// <summary>What became of a question.</summary>
public enum AskOutcome
{
    /// <summary>The question became a DSL query.</summary>
    Answered,

    /// <summary>The question was empty or longer than <see cref="QuestionPrompt.MaxQuestionLength"/>.</summary>
    Invalid,

    /// <summary>No Anthropic key: questions are off, the rest of the app works.</summary>
    NotConfigured,

    /// <summary>This person has asked <see cref="QuestionLimits.DailyLimit"/> questions today.</summary>
    LimitReached,

    /// <summary>The model refused or gave no valid query.</summary>
    NotUnderstood,

    /// <summary>The provider is busy or unreachable; worth trying again later.</summary>
    Unavailable,

    /// <summary>The provider refused the key or the request; logged for the owner.</summary>
    Failed,
}

public sealed record AskResult(AskOutcome Outcome, SearchQuery? Query = null);

/// <summary><c>Questions:DailyLimit</c>: questions per person per day (S-025), 200 unless configured.</summary>
public sealed record QuestionLimits(int DailyLimit = QuestionLimits.DefaultDailyLimit)
{
    public const int DefaultDailyLimit = 200;

    public static QuestionLimits From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var limit = configuration.GetValue("Questions:DailyLimit", DefaultDailyLimit);
        return limit >= 0 ? new QuestionLimits(limit) : throw new InvalidOperationException("Questions:DailyLimit cannot be negative.");
    }
}

/// <summary>Questions counted per person and day, with the tokens they cost (S-025).</summary>
public interface IQuestionQuota
{
    /// <summary>Counts one question for the day; false, and nothing counted, when <paramref name="limit"/> is reached.</summary>
    Task<bool> TryTakeAsync(string userKey, DateOnly day, int limit, CancellationToken cancellationToken = default);

    /// <summary>Adds the tokens a counted question cost.</summary>
    Task RecordAsync(string userKey, DateOnly day, TokenUsage usage, CancellationToken cancellationToken = default);

    /// <summary>Gives a counted question back, for one the provider never answered (nothing was billed).</summary>
    Task GiveBackAsync(string userKey, DateOnly day, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IQuestionQuota"/> in the catalog's <c>question_usage</c> table. Taking one is a single upsert that only
/// increments below the limit, so two questions at once cannot both take the last one. PostgreSQL's
/// <c>ON CONFLICT</c> (D-051: provider-specific SQL stays behind the project's own interface).
/// </summary>
public sealed class CatalogQuestionQuota(IDbContextFactory<CatalogDbContext> contexts) : IQuestionQuota
{
    public async Task<bool> TryTakeAsync(string userKey, DateOnly day, int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return false;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var taken = await db.Database.SqlQuery<int>($"""
            INSERT INTO question_usage (user_key, day, questions, input_tokens, output_tokens, cache_read_input_tokens, cache_creation_input_tokens)
            VALUES ({userKey}, {day}, 1, 0, 0, 0, 0)
            ON CONFLICT (user_key, day) DO UPDATE SET questions = question_usage.questions + 1
            WHERE question_usage.questions < {limit}
            RETURNING questions AS "Value"
            """).ToListAsync(cancellationToken);
        return taken.Count == 1;
    }

    public async Task RecordAsync(string userKey, DateOnly day, TokenUsage usage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usage);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.QuestionUsage.Where(u => u.UserKey == userKey && u.Day == day).ExecuteUpdateAsync(
            set => set
                .SetProperty(u => u.InputTokens, u => u.InputTokens + usage.InputTokens)
                .SetProperty(u => u.OutputTokens, u => u.OutputTokens + usage.OutputTokens)
                .SetProperty(u => u.CacheReadInputTokens, u => u.CacheReadInputTokens + usage.CacheReadInputTokens)
                .SetProperty(u => u.CacheCreationInputTokens, u => u.CacheCreationInputTokens + usage.CacheCreationInputTokens),
            cancellationToken);
    }

    public async Task GiveBackAsync(string userKey, DateOnly day, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.QuestionUsage.Where(u => u.UserKey == userKey && u.Day == day && u.Questions > 0)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.Questions, u => u.Questions - 1), cancellationToken);
    }
}

/// <summary>
/// A question from the web, start to end (S-025): the daily limit, the context (today and the catalog's names), the
/// translator, and the DSL's own parser on whatever comes back. Shared by the question box and <c>POST /api/questions</c>.
/// </summary>
public sealed class QuestionService(
    IQuestionTranslator translator,
    IQuestionQuota quota,
    ILibraryQueries library,
    QuestionLimits limits,
    TimeProvider clock,
    SearchOptions? searchOptions = null,
    QuestionWarmUp? warmUp = null)
{
    public bool IsConfigured => translator.IsConfigured;

    /// <summary>Starts the background warm-up of the question format when it is due (D-101); never waits or throws.</summary>
    public void WarmUp() => warmUp?.TryStart();

    public int DailyLimit => limits.DailyLimit;

    /// <summary>
    /// The DSL's own gate for the translator's answers: <see cref="SearchQueryJson.TryParse"/>, and none of the filters a
    /// question cannot express (<see cref="QuestionPrompt.NotFromQuestions"/>), which the output format leaves out anyway. <c>sourceCount</c> and
    /// <c>aggregate.sum</c> are part of an answer (D-103).
    /// </summary>
    public static string? Validate(string json) =>
        !SearchQueryJson.TryParse(json, out var query, out var error) ? error
        : query.Filters.SimilarTo is not null ? "filters.similarTo is not part of an answer: a question cannot name an asset."
        : query.Filters.Quality is not null ? "filters.quality is not part of an answer: the organize page shows blurry photos."
        : query.Filters.IncludeHidden ? "filters.includeHidden is not part of an answer: a question never asks for what was set aside."
        : query.Filters.EventIds is not null ? "filters.eventIds is not part of an answer: a question cannot name an occasion."
        : null;

    /// <summary>
    /// A person, for the daily limit, pseudonymously: the SHA-256 of their e-mail address, not the address (D-073). Null
    /// for a request without one, which the access list never lets through anyway.
    /// </summary>
    public static string? UserKey(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var email = user.FindFirstValue(ClaimTypes.Email);
        return string.IsNullOrWhiteSpace(email)
            ? null
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())));
    }

    /// <summary>
    /// What the translator is told besides the question (D-073, D-094). The occasion types are those the catalog's
    /// occasions have; before any occasion is typed (<c>events classify</c> has not run) all of them, so "how many
    /// weddings" still gets its type and counts typed occasions (none) rather than every occasion.
    /// </summary>
    public static QuestionContext Context(FilterOptions options, DateOnly today, string timeZone)
    {
        ArgumentNullException.ThrowIfNull(options);
        return QuestionContext.For(today, timeZone)
            .Add("sources", "filters.sources", options.Sources)
            .Add("cities", "filters.places.cities", options.Cities)
            .Add("countries", "filters.places.countries", options.Countries ?? [])
            .Add("saved places", "filters.savedPlaces", options.SavedPlaces)
            .Add("home place", "filters.savedPlaces", options.HomePlace is { } home ? [home] : [])
            .Add("people", "filters.people.all", options.People)
            .Add("occasion types", "filters.eventTypes", options.EventTypes.Count > 0 ? options.EventTypes : Sepiabox.Catalog.Events.OccasionTypes.All)
            .Build();
    }

    public async Task<AskResult> AskAsync(string? question, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!translator.IsConfigured)
        {
            return new AskResult(AskOutcome.NotConfigured);
        }

        var cleaned = QuestionPrompt.Clean(question);
        var userKey = UserKey(user);
        if (cleaned is null || userKey is null)
        {
            return new AskResult(AskOutcome.Invalid);
        }

        var zone = searchOptions?.TimeZone ?? clock.LocalTimeZone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        if (!await quota.TryTakeAsync(userKey, today, limits.DailyLimit, cancellationToken))
        {
            return new AskResult(AskOutcome.LimitReached);
        }

        var options = await library.GetFilterOptionsAsync(cancellationToken);
        // People's names, never an e-mail address or who said "This is me" (D-080); the family's places by name, and
        // which one is home, never where they are (D-083); the types the occasions have, which are a fixed list of English keys (S-024, D-094).
        var context = Context(options, today, zone.Id);

        var translation = await translator.TranslateAsync(cleaned, context, cancellationToken);
        if (translation.Outcome is QuestionOutcome.Unavailable or QuestionOutcome.Failed && translation.Usage == TokenUsage.None)
        {
            // The provider answered nothing, so nothing was billed: the question does not count.
            await quota.GiveBackAsync(userKey, today, CancellationToken.None);
        }
        else
        {
            // Recorded whatever the outcome: a refused or invalid answer was billed too.
            await quota.RecordAsync(userKey, today, translation.Usage, CancellationToken.None);
        }
        if (QuestionWarmUp.IsWarm(translation))
        {
            warmUp?.NoteSuccess();
        }

        return translation.Outcome switch
        {
            // Parsed again here: the translator's own check is not what the search trusts.
            QuestionOutcome.Translated when SearchQueryJson.TryParse(translation.Json!, out var query, out _) => new AskResult(AskOutcome.Answered, query),
            QuestionOutcome.Translated or QuestionOutcome.NotUnderstood => new AskResult(AskOutcome.NotUnderstood),
            QuestionOutcome.Unavailable => new AskResult(AskOutcome.Unavailable),
            QuestionOutcome.NotConfigured => new AskResult(AskOutcome.NotConfigured),
            _ => new AskResult(AskOutcome.Failed),
        };
    }
}
