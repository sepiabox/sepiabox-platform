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
    string? Place = null)
{
    /// <summary>The people in it (S-021): those with a counted face, named ones first, by name.</summary>
    public IReadOnlyList<AssetPerson> People { get; init; } = [];
}

/// <summary>A person in a photo; <see cref="Name"/> is null while they are unnamed.</summary>
public sealed record AssetPerson(long Id, string? Name);

/// <summary>The assets a search found, up to its limit; <see cref="HasMore"/> says whether more matched.</summary>
public sealed record SearchPage(IReadOnlyList<TimelineItem> Items, bool HasMore);

/// <summary>
/// What the filter bar offers: the years, cities and sources the catalog has, in display order. <c>Countries</c> are
/// the ISO codes of the countries the catalog's places are in, for the question translator (S-025).
/// </summary>
public sealed record FilterOptions(
    IReadOnlyList<int> Years, IReadOnlyList<string> Cities, IReadOnlyList<string> Sources, IReadOnlyList<string>? Countries = null)
{
    /// <summary>The named people with faces, in display order (S-021); read on every request, since names change.</summary>
    public IReadOnlyList<string> People { get; init; } = [];

    /// <summary>Whether the signed-in user said "This is me" for someone, so "me" can be filtered on.</summary>
    public bool MeLinked { get; init; }
}

/// <summary>What a download of the original needs: a name for the file and its content type.</summary>
public sealed record OriginalFile(string FileName, string ContentType);

/// <summary>
/// One person on the people page (S-019): <see cref="Name"/> is null while unnamed, <see cref="Cover"/> the face shown
/// (their best scored one), null when no thumbnail is there to crop.
/// </summary>
public sealed record PersonSummary(long Id, string? Name, int FaceCount, FaceCrop? Cover);

/// <summary>
/// One face on a person's page (S-020): its id for the corrections, its photo, its crop when there is a thumbnail, and,
/// among the excluded faces, whether the family said it is not a face.
/// </summary>
public sealed record PersonFace(long Id, string AssetId, FaceCrop? Crop, bool NotAFace = false);

/// <summary>
/// A person's page (S-020): their name, the user who said "This is me" (D-018), their faces (not excluded, best first,
/// up to <see cref="LibraryQueries.MaxPersonFaces"/>), the faces excluded from them (to give back or move), and the
/// other people they can be merged with or faces moved to.
/// </summary>
public sealed record PersonDetail(
    long Id,
    string? Name,
    string? UserEmail,
    int FaceCount,
    IReadOnlyList<PersonFace> Faces,
    IReadOnlyList<PersonSummary> Others,
    IReadOnlyList<PersonFace>? ExcludedFaces = null)
{
    public IReadOnlyList<PersonFace> ExcludedFaces { get; init; } = ExcludedFaces ?? [];

    /// <summary>A face of this person, shown or excluded; null when it is not theirs.</summary>
    public PersonFace? FindFace(long faceId) => Faces.Concat(ExcludedFaces).FirstOrDefault(f => f.Id == faceId);
}

public interface ILibraryQueries
{
    /// <summary>
    /// The people faces were grouped into, most faces first (then oldest first), up to <see cref="LibraryQueries.MaxPeople"/>;
    /// people left with no face are not listed.
    /// </summary>
    Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(CancellationToken cancellationToken = default);

    /// <summary>Null when the catalog has no person with that id.</summary>
    Task<PersonDetail?> GetPersonAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Newest first; assets without a capture date come last. <paramref name="page"/> starts at 1.</summary>
    Task<TimelinePage> GetTimelinePageAsync(int page, CancellationToken cancellationToken = default);

    /// <summary>Null when the catalog has no asset with that id.</summary>
    Task<AssetDetail?> GetAssetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>The blob path of one derivative, or null when the asset or that derivative does not exist.</summary>
    Task<string?> GetDerivativeBlobPathAsync(string id, string name, CancellationToken cancellationToken = default);

    /// <summary>Null when the catalog has no asset with that id.</summary>
    Task<OriginalFile?> GetOriginalAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The assets a valid search DSL query matches (S-013), in its order and up to its limit; with
    /// <c>filters.visual</c>, most similar first, or <see cref="VisualSearchUnavailableException"/> without the text model.
    /// </summary>
    Task<SearchPage> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// The count a query with <see cref="SearchQuery.Aggregate"/> asks for (S-026); null for one with a description,
    /// which is not counted in the MVP (D-070, D-075).
    /// </summary>
    Task<CountResult?> CountAsync(SearchQuery query, CancellationToken cancellationToken = default);

    Task<FilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default);
}

public sealed class LibraryQueries(
    IDbContextFactory<CatalogDbContext> contexts,
    TimeProvider? clock = null,
    SearchOptions? searchOptions = null,
    FilterOptionsCache? filterOptions = null,
    IQueryEmbedder? embedder = null,
    ICurrentUser? user = null) : ILibraryQueries
{
    public const int PageSize = 200;

    /// <summary>More people than a family has faces worth naming; the page shows the most photographed.</summary>
    public const int MaxPeople = 500;

    /// <summary>Faces shown on a person's page, best first.</summary>
    public const int MaxPersonFaces = 500;

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

        // Only counted faces put someone in a photo (D-077, D-079).
        var people = await db.Faces.AsNoTracking()
            .Where(f => f.AssetId == id && f.PersonId != null
                && (f.Assignment == FaceAssignments.Auto || f.Assignment == FaceAssignments.Manual))
            .Select(f => new AssetPerson(f.PersonId!.Value, f.Person!.Name))
            .Distinct()
            .ToListAsync(cancellationToken);
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
            PlaceNames.Format(asset.PlaceCity, asset.PlaceRegion, asset.PlaceCountry))
        {
            People = [.. people.OrderBy(p => p.Name is null).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Id)],
        };
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

    public async Task<CountResult?> CountAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await Search(db).CountAsync(query, cancellationToken);
    }

    public async Task<SearchPage> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        // A search by description comes back most similar first (S-016); any other by capture date.
        var rows = await Search(db).RunAsync(
            query,
            assets => assets.Select(a => new TimelineItem(
                a.Id, a.Kind, a.CapturedAt, a.Derivatives.Any(d => d.Name == DerivativeNames.Thumbnail), a.DurationSeconds)),
            scored => scored.Select(s => new TimelineItem(
                s.Asset.Id, s.Asset.Kind, s.Asset.CapturedAt, s.Asset.Derivatives.Any(d => d.Name == DerivativeNames.Thumbnail),
                s.Asset.DurationSeconds)),
            cancellationToken: cancellationToken);
        return new SearchPage([.. rows.Take(query.Limit)], rows.Count > query.Limit);
    }

    public async Task<IReadOnlyList<PersonSummary>> GetPeopleAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var people = await db.People.AsNoTracking()
            .Where(p => p.FaceCount > 0)
            .OrderByDescending(p => p.FaceCount).ThenBy(p => p.Id)
            .Take(MaxPeople)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.FaceCount,
                Cover = p.Faces
                    .Where(f => f.Assignment == FaceAssignments.Auto || f.Assignment == FaceAssignments.Manual)
                    .OrderByDescending(f => f.Score).ThenBy(f => f.Id)
                    .Select(f => new { f.AssetId, f.Box })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var thumbnails = await ThumbnailSizesAsync(db, [.. people.Where(p => p.Cover is not null).Select(p => p.Cover!.AssetId)], cancellationToken);
        return [.. people.Select(p => new PersonSummary(
            p.Id, p.Name, p.FaceCount, p.Cover is { } cover ? Crop(cover.AssetId, cover.Box, thumbnails) : null))];
    }

    public async Task<PersonDetail?> GetPersonAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (person is null)
        {
            return null;
        }

        var faces = await db.Faces.AsNoTracking()
            .Where(f => f.PersonId == id && (f.Assignment == FaceAssignments.Auto || f.Assignment == FaceAssignments.Manual))
            .OrderByDescending(f => f.Score).ThenBy(f => f.Id)
            .Take(MaxPersonFaces)
            .Select(f => new { f.Id, f.AssetId, f.Box })
            .ToListAsync(cancellationToken);
        var excluded = await db.Faces.AsNoTracking()
            .Where(f => f.PersonId == id && (f.Assignment == FaceAssignments.Excluded || f.Assignment == FaceAssignments.NotFace))
            .OrderByDescending(f => f.Score).ThenBy(f => f.Id)
            .Take(MaxPersonFaces)
            .Select(f => new { f.Id, f.AssetId, f.Box, NotAFace = f.Assignment == FaceAssignments.NotFace })
            .ToListAsync(cancellationToken);
        var thumbnails = await ThumbnailSizesAsync(db, [.. faces.Select(f => f.AssetId).Concat(excluded.Select(f => f.AssetId))], cancellationToken);
        // Anyone else with faces or a name can be the same person; most faces first, as on the people page.
        var others = await db.People.AsNoTracking()
            .Where(p => p.Id != id && (p.FaceCount > 0 || p.Name != null))
            .OrderByDescending(p => p.FaceCount).ThenBy(p => p.Id)
            .Take(MaxPeople)
            .Select(p => new PersonSummary(p.Id, p.Name, p.FaceCount, null))
            .ToListAsync(cancellationToken);

        return new PersonDetail(
            person.Id,
            person.Name,
            person.UserEmail,
            person.FaceCount,
            [.. faces.Select(f => new PersonFace(f.Id, f.AssetId, Crop(f.AssetId, f.Box, thumbnails)))],
            others,
            [.. excluded.Select(f => new PersonFace(f.Id, f.AssetId, Crop(f.AssetId, f.Box, thumbnails), f.NotAFace))]);
    }

    // Boxes are relative to the upright derivatives (D-047), so each thumbnail's own size maps them.
    private static async Task<Dictionary<string, (int Width, int Height)>> ThumbnailSizesAsync(
        CatalogDbContext db, List<string> assetIds, CancellationToken cancellationToken)
    {
        var assets = assetIds.Distinct().ToList();
        return await db.Derivatives.AsNoTracking()
            .Where(d => assets.Contains(d.AssetId) && d.Name == DerivativeNames.Thumbnail && d.Width > 0 && d.Height > 0)
            .ToDictionaryAsync(d => d.AssetId, d => (d.Width, d.Height), StringComparer.Ordinal, cancellationToken);
    }

    private static FaceCrop? Crop(string assetId, double[]? box, Dictionary<string, (int Width, int Height)> thumbnails) =>
        thumbnails.TryGetValue(assetId, out var size) && box is { Length: 4 } ? new FaceCrop(assetId, box, size.Width, size.Height) : null;

    public async Task<FilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        // Loaded unsorted and shared; sorted per request, in the reader's culture.
        var options = await (filterOptions?.GetAsync(() => LoadFilterOptionsAsync(CancellationToken.None))
            ?? LoadFilterOptionsAsync(cancellationToken));
        var byName = StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, ignoreCase: true);
        // People are renamed and merged at any time (S-020), so they are read now rather than cached.
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var people = await db.People.AsNoTracking().Where(p => p.Name != null && p.FaceCount > 0).Select(p => p.Name!)
            .ToListAsync(cancellationToken);
        var me = Me();
        var meLinked = me is not null && await db.People.AsNoTracking().AnyAsync(p => p.UserEmail == me, cancellationToken);
        return options with
        {
            Cities = [.. options.Cities.Order(byName)],
            Sources = [.. options.Sources.Order(byName)],
            People = [.. people.Order(byName)],
            MeLinked = meLinked,
        };
    }

    // The signed-in user's address as "This is me" stores it (lower-cased, S-020).
    private string? Me() => user?.Email is { } email && !string.IsNullOrWhiteSpace(email) ? email.Trim().ToLowerInvariant() : null;

    private SearchService Search(CatalogDbContext db) => new(db, clock ?? TimeProvider.System, searchOptions, embedder, user?.Email);

    private async Task<FilterOptions> LoadFilterOptionsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var years = await db.Assets.Where(a => a.CapturedAt != null).Select(a => a.CapturedAt!.Value.Year)
            .Distinct().OrderByDescending(y => y).ToListAsync(cancellationToken);
        var cities = await db.Assets.Where(a => a.PlaceCity != null).Select(a => a.PlaceCity!)
            .Distinct().ToListAsync(cancellationToken);
        var sources = await db.AssetSources.Select(s => s.Name).Distinct().ToListAsync(cancellationToken);
        // The catalog keeps country names (D-066); GeoNames gives their codes, which the DSL takes.
        var countryNames = db.Assets.Where(a => a.PlaceCountry != null).Select(a => a.PlaceCountry);
        var countries = await db.GeoNames.Where(g => g.CountryName != null && countryNames.Contains(g.CountryName))
            .Select(g => g.CountryCode).Distinct().OrderBy(c => c).ToListAsync(cancellationToken);
        return new FilterOptions(years, cities, sources, countries);
    }

    private static IEnumerable<AssetSource> OrderSources(IEnumerable<AssetSource> sources) =>
        sources.OrderBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.Path, StringComparer.Ordinal);

    private sealed record TimelineRow(string Id, string Kind, DateTime? CapturedAt, double? DurationSeconds, bool HasThumbnail);
}
