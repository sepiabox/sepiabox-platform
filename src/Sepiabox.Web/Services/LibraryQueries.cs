using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;

namespace Sepiabox.Web.Services;

/// <summary>One thumbnail on the timeline.</summary>
public sealed record TimelineItem(string Id, string Kind, DateTime? CapturedAt, string? ThumbnailBlobPath);

/// <summary>A page of the timeline; <see cref="HasMore"/> says whether an older page exists.</summary>
public sealed record TimelinePage(int Page, IReadOnlyList<TimelineItem> Items, bool HasMore);

public sealed record AssetSourceDetail(string Type, string Name, string Path);

public sealed record AssetDetail(
    string Id,
    string Kind,
    DateTime? CapturedAt,
    string? CameraMake,
    string? CameraModel,
    int? Width,
    int? Height,
    string? PreviewBlobPath,
    IReadOnlyList<AssetSourceDetail> Sources);

public interface ILibraryQueries
{
    /// <summary>Newest first; assets without a capture date come last. <paramref name="page"/> starts at 1.</summary>
    Task<TimelinePage> GetTimelinePageAsync(int page, CancellationToken cancellationToken = default);

    /// <summary>Null when the catalog has no asset with that id.</summary>
    Task<AssetDetail?> GetAssetAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class LibraryQueries(IDbContextFactory<CatalogDbContext> contexts) : ILibraryQueries
{
    public const int PageSize = 200;

    // Names written by `process` (D-015); a video's poster stands in for the photo preview.
    private const string ThumbnailName = "thumb-256";
    private static readonly string[] s_previewNames = ["preview-1280", "poster"];

    public async Task<TimelinePage> GetTimelinePageAsync(int page, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // One extra row tells whether another page follows without counting the whole library.
        var rows = await db.Assets
            .AsNoTracking()
            .OrderBy(a => a.CapturedAt == null)
            .ThenByDescending(a => a.CapturedAt)
            .ThenBy(a => a.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize + 1)
            .Select(a => new TimelineItem(
                a.Id,
                a.Kind,
                a.CapturedAt,
                a.Derivatives.Where(d => d.Name == ThumbnailName).Select(d => d.BlobPath).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > PageSize;
        return new TimelinePage(page, hasMore ? rows[..PageSize] : rows, hasMore);
    }

    public async Task<AssetDetail?> GetAssetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var asset = await db.Assets
            .AsNoTracking()
            .AsSplitQuery()
            .Include(a => a.Sources)
            .Include(a => a.Derivatives)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (asset is null)
        {
            return null;
        }

        var preview = s_previewNames
            .Select(name => asset.Derivatives.FirstOrDefault(d => d.Name == name))
            .FirstOrDefault(d => d is not null);

        return new AssetDetail(
            asset.Id,
            asset.Kind,
            asset.CapturedAt,
            asset.CameraMake,
            asset.CameraModel,
            asset.Width,
            asset.Height,
            preview?.BlobPath,
            [.. asset.Sources
                .OrderBy(s => s.Name, StringComparer.Ordinal)
                .ThenBy(s => s.Path, StringComparer.Ordinal)
                .Select(s => new AssetSourceDetail(s.Type, s.Name, s.Path))]);
    }
}
