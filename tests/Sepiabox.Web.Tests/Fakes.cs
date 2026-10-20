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

    public Task<SearchPage> SearchAsync(Sepiabox.Catalog.Search.SearchQuery query, CancellationToken cancellationToken = default)
    {
        Searches.Add(query);
        return Task.FromResult(SearchResults);
    }

    public Task<FilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Options);
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
