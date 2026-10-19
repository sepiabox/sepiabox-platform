using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>A clock that stands still, so SAS expiry times can be asserted exactly.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class FakeLibrary(params TimelineItem[] items) : ILibraryQueries
{
    public bool HasMore { get; init; }

    public Task<TimelinePage> GetTimelinePageAsync(int page, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TimelinePage(page, items, HasMore));

    public Task<AssetDetail?> GetAssetAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult<AssetDetail?>(null);
}

internal sealed class FakeLinks : ISasUrlFactory
{
    public Task<Uri> ForDerivativeAsync(string blobPath, CancellationToken cancellationToken = default) =>
        Task.FromResult(new Uri($"https://storage.test/derivatives/{blobPath}?sig=x"));

    public Task<Uri> ForOriginalAsync(string assetId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new Uri($"https://storage.test/originals/{assetId}?sig=x"));
}

internal static class Ids
{
    /// <summary>A 64-character lowercase hex id, like a SHA-256, made unique by <paramref name="n"/>.</summary>
    public static string Of(int n) => n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
}
