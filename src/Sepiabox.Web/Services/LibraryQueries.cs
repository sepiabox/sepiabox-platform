using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Search;

namespace Sepiabox.Web.Services;

/// <summary>One thumbnail on the timeline; <c>DurationSeconds</c> is null for photos and videos of unknown length.</summary>
public sealed record TimelineItem(string Id, string Kind, DateTime? CapturedAt, bool HasThumbnail, double? DurationSeconds);

/// <summary>A page of the timeline; <see cref="HasMore"/> says whether an older page exists.</summary>
public sealed record TimelinePage(int Page, IReadOnlyList<TimelineItem> Items, bool HasMore);

public sealed record AssetSourceDetail(string Type, string Name, string Path);

/// <summary>
/// One asset for the detail page. <c>PreviewName</c> is the derivative shown large: the photo preview, or the
/// video poster. <c>HasProxy</c> says whether a video's 720p version exists, so it can be played. <c>Place</c> is
/// the city, region and country GeoNames named (S-012), as one line.
/// </summary>
public sealed record AssetDetail(
    string Id,
    string Kind,
    DateTime? CapturedAt,
    string? CameraMake,
    string? CameraModel,
    int? Width,
    int? Height,
    string? PreviewName,
    IReadOnlyList<AssetSourceDetail> Sources,
    double? DurationSeconds,
    bool HasProxy,
    string? Place = null);

/// <summary>The assets a search found, up to its limit; <see cref="HasMore"/> says whether more matched.</summary>
public sealed record SearchPage(IReadOnlyList<TimelineItem> Items, bool HasMore);

/// <summary>What the filter bar offers: the years, cities and sources the catalog has, in display order.</summary>
public sealed record FilterOptions(IReadOnlyList<int> Years, IReadOnlyList<string> Cities, IReadOnlyList<string> Sources);

/// <summary>What a download of the original needs: a name for the file and its content type.</summary>
public sealed record OriginalFile(string FileName, string ContentType);

public interface ILibraryQueries
{
    /// <summary>Newest first; assets without a capture date come last. <paramref name="page"/> starts at 1.</summary>
    Task<TimelinePage> GetTimelinePageAsync(int page, CancellationToken cancellationToken = default);

    /// <summary>Null when the catalog has no asset with that id.</summary>
    Task<AssetDetail?> GetAssetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>The blob path of one derivative, or null when the asset or that derivative does not exist.</summary>
    Task<string?> GetDerivativeBlobPathAsync(string id, string name, CancellationToken cancellationToken = default);

    /// <summary>Null when the catalog has no asset with that id.</summary>
    Task<OriginalFile?> GetOriginalAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>The assets a valid search DSL query matches (S-013), in its order and up to its limit.</summary>
    Task<SearchPage> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);

    Task<FilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default);
}

public sealed class LibraryQueries(
    IDbContextFactory<CatalogDbContext> contexts,
    TimeProvider? clock = null,
    SearchOptions? searchOptions = null,
    FilterOptionsCache? filterOptions = null) : ILibraryQueries
{
    public const int PageSize = 200;

    /// <summary>Far beyond any family library (20 million assets); keeps a hand-typed page number sane.</summary>
    public const int MaxPage = 100_000;

    public static int ClampPage(long page) => (int)Math.Clamp(page, 1, MaxPage);

    /// <summary>A page number typed in a URL: anything that is not a whole number is page 1.</summary>
    public static int ParsePage(string? value) =>
        long.TryParse(value, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var page)
            ? ClampPage(page)
            : 1;

    /// <summary>One row beyond the page means more follow, unless the page is already the last allowed.</summary>
    public static bool HasNextPage(int page, int rowsFetched) => rowsFetched > PageSize && page < MaxPage;

    // File extensions for common photo and video content types; any other type downloads as the bare id.
    private static readonly Dictionary<string, string> s_extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
        ["image/heic"] = ".heic",
        ["image/heif"] = ".heif",
        ["image/tiff"] = ".tif",
        ["video/mp4"] = ".mp4",
        ["video/quicktime"] = ".mov",
        ["video/x-msvideo"] = ".avi",
        ["video/x-matroska"] = ".mkv",
        ["video/mpeg"] = ".mpg",
        ["video/mp2t"] = ".mts",
        ["video/dv"] = ".dv",
        // The pre-IANA name for DV, which other tools and cloud sources still use for the same files.
        ["video/x-dv"] = ".dv",
    };

    /// <summary>
    /// The file name a download gets: the source's own name, or the asset id with an extension for its
    /// content type when no source has a usable name.
    /// </summary>
    public static string DownloadName(string? sourcePath, string id, string contentType)
    {
        var fileName = sourcePath?[(sourcePath.LastIndexOfAny(['/', '\\']) + 1)..];
        return string.IsNullOrWhiteSpace(fileName)
            ? id + s_extensions.GetValueOrDefault(contentType, string.Empty)
            : fileName;
    }

    public async Task<TimelinePage> GetTimelinePageAsync(int page, CancellationToken cancellationToken = default)
    {
        page = ClampPage(page);
        long offset = (page - 1L) * PageSize;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // Written in SQL because LINQ cannot say NULLS LAST, and this order must match ix_assets_timeline
        // exactly for a page to be an index range scan. One extra row tells whether another page follows.
        var rows = await db.Database.SqlQuery<TimelineRow>($"""
            SELECT a.id, a.kind, a.captured_at, a.duration_seconds,
                   EXISTS (SELECT 1 FROM derivatives d WHERE d.asset_id = a.id AND d.name = {DerivativeNames.Thumbnail}) AS has_thumbnail
            FROM assets a
            ORDER BY a.captured_at DESC NULLS LAST, a.id
            OFFSET {offset} LIMIT {PageSize + 1}
            """).ToListAsync(cancellationToken);

        var items = rows.Take(PageSize).Select(r => new TimelineItem(r.Id, r.Kind, r.CapturedAt, r.HasThumbnail, r.DurationSeconds)).ToList();
        return new TimelinePage(page, items, HasNextPage(page, rows.Count));
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

        string[] previewOrder = [DerivativeNames.Preview, DerivativeNames.Poster];
        var preview = previewOrder.FirstOrDefault(name => asset.Derivatives.Any(d => d.Name == name));

        return new AssetDetail(
            asset.Id,
            asset.Kind,
            asset.CapturedAt,
            asset.CameraMake,
            asset.CameraModel,
            asset.Width,
            asset.Height,
            preview,
            [.. OrderSources(asset.Sources).Select(s => new AssetSourceDetail(s.Type, s.Name, s.Path))],
            asset.DurationSeconds,
            asset.Derivatives.Any(d => d.Name == DerivativeNames.Proxy),
            PlaceNames.Format(asset.PlaceCity, asset.PlaceRegion, asset.PlaceCountry));
    }

    public async Task<string?> GetDerivativeBlobPathAsync(string id, string name, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Derivatives
            .AsNoTracking()
            .Where(d => d.AssetId == id && d.Name == name)
            .Select(d => d.BlobPath)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<OriginalFile?> GetOriginalAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var asset = await db.Assets
            .AsNoTracking()
            .Include(a => a.Sources)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (asset is null)
        {
            return null;
        }

        // The first source in the order the detail page lists them names the download.
        var path = OrderSources(asset.Sources).FirstOrDefault()?.Path;
        return new OriginalFile(DownloadName(path, asset.Id, asset.ContentType), asset.ContentType);
    }

    public async Task<SearchPage> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await new SearchService(db, clock ?? TimeProvider.System, searchOptions)
            .Query(query)
            .Select(a => new TimelineItem(
                a.Id, a.Kind, a.CapturedAt, a.Derivatives.Any(d => d.Name == DerivativeNames.Thumbnail), a.DurationSeconds))
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken);
        return new SearchPage([.. rows.Take(query.Limit)], rows.Count > query.Limit);
    }

    public async Task<FilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        // Loaded unsorted and shared; sorted per request, in the reader's culture.
        var options = await (filterOptions?.GetAsync(() => LoadFilterOptionsAsync(CancellationToken.None))
            ?? LoadFilterOptionsAsync(cancellationToken));
        var byName = StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, ignoreCase: true);
        return new FilterOptions(options.Years, [.. options.Cities.Order(byName)], [.. options.Sources.Order(byName)]);
    }

    private async Task<FilterOptions> LoadFilterOptionsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var years = await db.Assets.Where(a => a.CapturedAt != null).Select(a => a.CapturedAt!.Value.Year)
            .Distinct().OrderByDescending(y => y).ToListAsync(cancellationToken);
        var cities = await db.Assets.Where(a => a.PlaceCity != null).Select(a => a.PlaceCity!)
            .Distinct().ToListAsync(cancellationToken);
        var sources = await db.AssetSources.Select(s => s.Name).Distinct().ToListAsync(cancellationToken);
        return new FilterOptions(years, cities, sources);
    }

    private static IEnumerable<AssetSource> OrderSources(IEnumerable<AssetSource> sources) =>
        sources.OrderBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.Path, StringComparer.Ordinal);

    private sealed record TimelineRow(string Id, string Kind, DateTime? CapturedAt, double? DurationSeconds, bool HasThumbnail);
}
