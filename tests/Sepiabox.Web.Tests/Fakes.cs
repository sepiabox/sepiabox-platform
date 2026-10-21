using Azure;
using Microsoft.Extensions.Logging;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>A clock that stands still, so SAS expiry times can be asserted exactly.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>A clock a test moves forward by hand.</summary>
internal sealed class MovableTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// An in-memory library. Items are split into pages of <see cref="PageSize"/>; the last requested page
/// is recorded so tests can see how a page number from the URL was clamped.
/// </summary>
internal sealed class FakeLibrary(params TimelineItem[] items) : ILibraryQueries
{
    public int PageSize { get; init; } = LibraryQueries.PageSize;

    public Dictionary<string, AssetDetail> Assets { get; } = [];

    public int? RequestedPage { get; private set; }

    /// <summary>Thrown by the timeline query, to reach the error page.</summary>
    public Exception? TimelineFailure { get; init; }

    public Task<TimelinePage> GetTimelinePageAsync(int page, CancellationToken cancellationToken = default)
    {
        if (TimelineFailure is not null)
        {
            return Task.FromException<TimelinePage>(TimelineFailure);
        }

        RequestedPage = page;
        var slice = items.Skip((page - 1) * PageSize).Take(PageSize).ToList();
        return Task.FromResult(new TimelinePage(page, slice, items.Length > page * PageSize));
    }

    public Task<AssetDetail?> GetAssetAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Assets.GetValueOrDefault(id));

    public Task<string?> GetDerivativeBlobPathAsync(string id, string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(Assets.ContainsKey(id) ? $"{id}/{name}{(name == DerivativeNames.Proxy ? ".mp4" : ".webp")}" : null);

    public Task<OriginalFile?> GetOriginalAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Assets.ContainsKey(id) ? new OriginalFile("IMG_0001.jpg", "image/jpeg") : null);

    /// <summary>What the filter bar offers.</summary>
    public FilterOptions Options { get; init; } = new([2021, 2019], ["Lisbon", "Porto"], ["HD da Ana"]);

    /// <summary>The searches asked for, in order; each returns <see cref="SearchResults"/>.</summary>
    public List<Sepiabox.Catalog.Search.SearchQuery> Searches { get; } = [];

    public SearchPage SearchResults { get; init; } = new([], false);

    /// <summary>Thrown by every search, such as the text model being unavailable.</summary>
    public Exception? SearchFailure { get; init; }

    /// <summary>What every count returns; null, as for a count with a description.</summary>
    public Sepiabox.Catalog.Search.CountResult? CountResult { get; init; }

    /// <summary>The counts asked for, in order.</summary>
    public List<Sepiabox.Catalog.Search.SearchQuery> Counts { get; } = [];

    public Task<Sepiabox.Catalog.Search.CountResult?> CountAsync(Sepiabox.Catalog.Search.SearchQuery query, CancellationToken cancellationToken = default)
    {
        Counts.Add(query);
        return Task.FromResult(CountResult);
    }

    public Task<SearchPage> SearchAsync(Sepiabox.Catalog.Search.SearchQuery query, CancellationToken cancellationToken = default)
    {
        Searches.Add(query);
        return SearchFailure is null ? Task.FromResult(SearchResults) : Task.FromException<SearchPage>(SearchFailure);
    }

    public Task<FilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Options);

    /// <summary>The people page's people, as the query returns them.</summary>
    public List<PersonSummary> People { get; init; } = [];

    public Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PersonSummary>>(People);
}

/// <summary>
/// Signing that fails the ways Azure can: storage refuses the delegation key, or the managed identity
/// cannot get a token at all.
/// </summary>
internal sealed class FailingLinks(Exception? failure = null) : ISasUrlFactory
{
    private readonly Exception _failure = failure ?? new RequestFailedException(403, "AuthorizationPermissionMismatch");

    public Task<Uri> ForDerivativeAsync(string blobPath, CancellationToken cancellationToken = default) =>
        Task.FromException<Uri>(_failure);

    public Task<Uri> ForVideoAsync(string blobPath, CancellationToken cancellationToken = default) =>
        Task.FromException<Uri>(_failure);

    public Task<Uri> ForOriginalAsync(string assetId, OriginalFile file, CancellationToken cancellationToken = default) =>
        Task.FromException<Uri>(_failure);
}

internal static class Ids
{
    /// <summary>A 64-character lowercase hex id, like a SHA-256, made unique by <paramref name="n"/>.</summary>
    public static string Of(int n) => n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
}

internal static class Details
{
    public static AssetDetail Photo(string id, DateTime? capturedAt = null, string? previewName = DerivativeNames.Preview) =>
        new(id, AssetKinds.Photo, capturedAt, "Apple", "iPhone 15", 4000, 3000, previewName,
            [new AssetSourceDetail("local", "HD Ana", "2019/praia.jpg")], DurationSeconds: null, HasProxy: false);

    /// <summary>A 3-second video; by default it has its poster and its 720p version.</summary>
    public static AssetDetail Video(string id, bool hasProxy = true, string? posterName = DerivativeNames.Poster) =>
        new(id, AssetKinds.Video, new DateTime(2020, 8, 9, 18, 5, 0), "JVC", "GZ-MG330", 1024, 576, posterName,
            [new AssetSourceDetail("local", "HD Ana", "2020/MOV001.MOD")], DurationSeconds: 3, HasProxy: hasProxy);
}

/// <summary>Keeps what was logged, so a test can check a warning.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}

/// <summary>
/// A stand-in for the CLIP text encoder (S-016): unit tests never need the real model. Without a function it
/// returns a fixed unit vector; unavailable, it behaves like a web without the text model.
/// </summary>
internal sealed class FakeQueryEmbedder(bool available = true, Func<string, float[]>? embed = null) : Sepiabox.Catalog.Search.IQueryEmbedder
{
    public List<string> Texts { get; } = [];

    public bool IsAvailable => available;

    public float[] Embed(string text)
    {
        if (!available)
        {
            throw new Sepiabox.Catalog.Search.VisualSearchUnavailableException("No text model in this test.");
        }

        Texts.Add(text);
        return embed?.Invoke(text) ?? Axis(0);
    }

    /// <summary>A 512-value unit vector along <paramref name="axis"/>.</summary>
    public static float[] Axis(int axis, float weight = 1, int other = -1, float otherWeight = 0)
    {
        var vector = new float[512];
        vector[axis] = weight;
        if (other >= 0)
        {
            vector[other] = otherWeight;
        }

        var norm = (float)Math.Sqrt(vector.Sum(v => v * v));
        return [.. vector.Select(v => v / norm)];
    }
}

/// <summary>A translator that answers every question with <see cref="Json"/>, or with <see cref="Outcome"/>.</summary>
internal sealed class FakeTranslator : Sepiabox.Search.Questions.IQuestionTranslator
{
    public bool IsConfigured { get; init; } = true;

    public string Json { get; init; } = """{"filters": {}}""";

    public Sepiabox.Search.Questions.QuestionOutcome Outcome { get; init; } = Sepiabox.Search.Questions.QuestionOutcome.Translated;

    public Sepiabox.Search.Questions.TokenUsage Usage { get; init; } = new(100, 20, 80, 0);

    /// <summary>The questions and contexts it was given, in order.</summary>
    public List<(string Question, Sepiabox.Search.Questions.QuestionContext Context)> Calls { get; } = [];

    public Task<Sepiabox.Search.Questions.QuestionTranslation> TranslateAsync(
        string question, Sepiabox.Search.Questions.QuestionContext context, CancellationToken cancellationToken = default)
    {
        lock (Calls)
        {
            Calls.Add((question, context));
        }

        return Task.FromResult(new Sepiabox.Search.Questions.QuestionTranslation(
            Outcome, Outcome == Sepiabox.Search.Questions.QuestionOutcome.Translated ? Json : null, Usage, 1));
    }
}

/// <summary>The daily question counter in memory.</summary>
internal sealed class FakeQuestionQuota : IQuestionQuota
{
    public Dictionary<(string UserKey, DateOnly Day), (int Questions, Sepiabox.Search.Questions.TokenUsage Usage)> Days { get; } = [];

    public Task<bool> TryTakeAsync(string userKey, DateOnly day, int limit, CancellationToken cancellationToken = default)
    {
        lock (Days)
        {
            var (questions, usage) = Days.GetValueOrDefault((userKey, day), (0, Sepiabox.Search.Questions.TokenUsage.None));
            if (questions >= limit)
            {
                return Task.FromResult(false);
            }

            Days[(userKey, day)] = (questions + 1, usage);
            return Task.FromResult(true);
        }
    }

    public Task GiveBackAsync(string userKey, DateOnly day, CancellationToken cancellationToken = default)
    {
        lock (Days)
        {
            var (questions, usage) = Days[(userKey, day)];
            Days[(userKey, day)] = (questions - 1, usage);
            return Task.CompletedTask;
        }
    }

    public Task RecordAsync(string userKey, DateOnly day, Sepiabox.Search.Questions.TokenUsage usage, CancellationToken cancellationToken = default)
    {
        lock (Days)
        {
            var (questions, total) = Days[(userKey, day)];
            Days[(userKey, day)] = (questions, total.Add(usage));
            return Task.CompletedTask;
        }
    }
}
