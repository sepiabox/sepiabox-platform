using Azure;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>A clock that stands still, so SAS expiry times can be asserted exactly.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
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

    public Task<TimelinePage> GetTimelinePageAsync(int page, CancellationToken cancellationToken = default)
    {
        RequestedPage = page;
        var slice = items.Skip((page - 1) * PageSize).Take(PageSize).ToList();
        return Task.FromResult(new TimelinePage(page, slice, items.Length > page * PageSize));
    }

    public Task<AssetDetail?> GetAssetAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Assets.GetValueOrDefault(id));

    public Task<string?> GetDerivativeBlobPathAsync(string id, string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(Assets.ContainsKey(id) ? $"{id}/{name}.webp" : null);

    public Task<OriginalFile?> GetOriginalAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Assets.ContainsKey(id) ? new OriginalFile("IMG_0001.jpg", "image/jpeg") : null);
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
            [new AssetSourceDetail("local", "HD Ana", "2019/praia.jpg")]);
}
