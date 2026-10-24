using Microsoft.EntityFrameworkCore;
using Sepiabox.Catalog;
using Sepiabox.Catalog.Organization;
using Sepiabox.Catalog.Search;

namespace Sepiabox.Web.Services;

/// <summary>A photo on the organize page: its tile, and how sharp it is.</summary>
public sealed record OrganizeItem(TimelineItem Item, double? Sharpness, string? PreviewName = null)
{
    /// <summary>The largest preview the asset has, in the detail page's order (preview-2048, then preview-1280): an index-only photo has only the first.</summary>
    public static string? LargestPreview(IEnumerable<string> names) =>
        names.Contains(DerivativeNames.LargePreview) ? DerivativeNames.LargePreview : names.Contains(DerivativeNames.Preview) ? DerivativeNames.Preview : null;
}

/// <summary>A burst (S-027): photos taken within seconds that look alike, in capture order, and the one to keep.</summary>
public sealed record BurstGroup(IReadOnlyList<OrganizeItem> Items, string KeepId);

/// <summary>A page of an organize list; <see cref="HasMore"/> says whether another follows, <see cref="Total"/> how many there are in all.</summary>
public sealed record OrganizePage<T>(int Page, IReadOnlyList<T> Items, bool HasMore, int Total);

/// <summary>
/// The organize page's lists and its one action (S-027, D-098): bursts, blurry photos, what was set aside, and setting
/// aside or bringing back. Nothing is deleted, and an original is never touched.
/// </summary>
/// <summary>The numbers on the organize tabs that are cheap to count (S-075).</summary>
public sealed record OrganizeCounts(int Blurry, int Hidden);

public interface IOrganization
{
    /// <summary>The sharpness below which a photo is blurry (<c>Search:BlurryBelow</c>).</summary>
    double BlurryBelow { get; }

    /// <summary>Bursts of photos that are not hidden, the newest first, <see cref="Organization.BurstsPageSize"/> to a page.</summary>
    Task<OrganizePage<BurstGroup>> GetBurstsAsync(int page, CancellationToken cancellationToken = default);

    /// <summary>Photos that are measured, not hidden and below the threshold, the blurriest first.</summary>
    Task<OrganizePage<OrganizeItem>> GetBlurryAsync(int page, CancellationToken cancellationToken = default);

    /// <summary>What was set aside, the newest capture first.</summary>
    Task<OrganizePage<OrganizeItem>> GetHiddenAsync(int page, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many photos are blurry and how many are set aside (S-075), for the numbers on the tabs. The bursts are not counted here: finding
    /// them reads every measured photo, so their number is shown when that list is (<see cref="OrganizePage{T}.Total"/>).
    /// </summary>
    Task<OrganizeCounts> GetCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets these assets aside (hidden); returns how many changed.</summary>
    Task<int> HideAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default);

    /// <summary>Brings these assets back; returns how many changed.</summary>
    Task<int> UnhideAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default);
}

/// <summary><see cref="IOrganization"/> on the catalog.</summary>
public sealed class Organization(IDbContextFactory<CatalogDbContext> contexts, SearchOptions? options = null) : IOrganization
{
    public const int BurstsPageSize = 20;
    public const int PhotosPageSize = 60;

    /// <summary>The most ids one action takes: a page of photos, with room.</summary>
    public const int MaxIdsPerAction = 500;

    public double BlurryBelow => options?.BlurryBelow ?? SearchOptions.DefaultBlurryBelow;

    public async Task<OrganizePage<BurstGroup>> GetBurstsAsync(int page, CancellationToken cancellationToken = default)
    {
        page = LibraryQueries.ClampPage(page);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.Assets.AsNoTracking()
            .Where(a => !a.Hidden && a.Kind == "photo" && a.DHash != null && a.CapturedAt != null && a.Sharpness != null)
            .Select(a => new { a.Id, CapturedAt = a.CapturedAt!.Value, DHash = a.DHash!, Sharpness = a.Sharpness!.Value })
            .ToListAsync(cancellationToken);
        var bursts = BurstFinder.Find(rows.Select(r => new BurstPhoto(r.Id, r.CapturedAt, r.DHash, r.Sharpness)))
            .OrderByDescending(b => b[0].CapturedAt).ThenBy(b => b[0].Id, StringComparer.Ordinal)
            .ToList();
        var shown = bursts.Skip((page - 1) * BurstsPageSize).Take(BurstsPageSize).ToList();
        var shownIds = shown.SelectMany(b => b.Select(p => p.Id)).ToList();
        var items = await ItemsAsync(db, shownIds, cancellationToken);
        var previews = (await db.Derivatives.AsNoTracking()
            .Where(d => shownIds.Contains(d.AssetId) && (d.Name == DerivativeNames.LargePreview || d.Name == DerivativeNames.Preview))
            .Select(d => new { d.AssetId, d.Name }).ToListAsync(cancellationToken))
            .GroupBy(d => d.AssetId).ToDictionary(g => g.Key, g => OrganizeItem.LargestPreview(g.Select(d => d.Name).ToList()), StringComparer.Ordinal);
        return new OrganizePage<BurstGroup>(
            page,
            [.. shown.Select(b => new BurstGroup(
                [.. b.Select(p => new OrganizeItem(items[p.Id], p.Sharpness, previews.GetValueOrDefault(p.Id)))], BurstFinder.Sharpest(b).Id))],
            bursts.Count > page * BurstsPageSize,
            bursts.Count);
    }

    public async Task<OrganizePage<OrganizeItem>> GetBlurryAsync(int page, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var below = BlurryBelow;
        return await PageAsync(
            db, page,
            db.Assets.AsNoTracking().Where(a => !a.Hidden && a.Sharpness != null && a.Sharpness < below),
            ordered => ordered.OrderBy(a => a.Sharpness).ThenBy(a => a.Id),
            cancellationToken);
    }

    public async Task<OrganizePage<OrganizeItem>> GetHiddenAsync(int page, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await PageAsync(
            db, page,
            db.Assets.AsNoTracking().Where(a => a.Hidden),
            ordered => ordered.OrderBy(a => a.CapturedAt == null).ThenByDescending(a => a.CapturedAt).ThenBy(a => a.Id),
            cancellationToken);
    }

    public async Task<OrganizeCounts> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var below = BlurryBelow;
        var blurry = await db.Assets.AsNoTracking().CountAsync(a => !a.Hidden && a.Sharpness != null && a.Sharpness < below, cancellationToken);
        var hidden = await db.Assets.AsNoTracking().CountAsync(a => a.Hidden, cancellationToken);
        return new OrganizeCounts(blurry, hidden);
    }

    public Task<int> HideAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => SetAsync(ids, true, cancellationToken);

    public Task<int> UnhideAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default) => SetAsync(ids, false, cancellationToken);

    private async Task<int> SetAsync(IReadOnlyCollection<string> ids, bool hidden, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.Where(AssetIds.IsValid).Distinct(StringComparer.Ordinal).Take(MaxIdsPerAction).ToList();
        if (wanted.Count == 0)
        {
            return 0;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Assets.Where(a => wanted.Contains(a.Id) && a.Hidden != hidden)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Hidden, hidden), cancellationToken);
    }

    private static async Task<OrganizePage<OrganizeItem>> PageAsync(
        CatalogDbContext db, int page, IQueryable<Asset> assets, Func<IQueryable<Asset>, IOrderedQueryable<Asset>> order, CancellationToken cancellationToken)
    {
        page = LibraryQueries.ClampPage(page);
        var total = await assets.CountAsync(cancellationToken);
        var rows = await order(assets).Skip((page - 1) * PhotosPageSize).Take(PhotosPageSize)
            .Select(a => new OrganizeItem(
                new TimelineItem(a.Id, a.Kind, a.CapturedAt, a.Derivatives.Any(d => d.Name == DerivativeNames.Thumbnail), a.DurationSeconds), a.Sharpness, a.Derivatives.Any(d => d.Name == DerivativeNames.LargePreview) ? DerivativeNames.LargePreview
                    : a.Derivatives.Any(d => d.Name == DerivativeNames.Preview) ? DerivativeNames.Preview : null))
            .ToListAsync(cancellationToken);
        return new OrganizePage<OrganizeItem>(page, rows, total > page * PhotosPageSize, total);
    }

    private static async Task<Dictionary<string, TimelineItem>> ItemsAsync(CatalogDbContext db, List<string> ids, CancellationToken cancellationToken) =>
        await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id))
            .Select(a => new TimelineItem(a.Id, a.Kind, a.CapturedAt, a.Derivatives.Any(d => d.Name == DerivativeNames.Thumbnail), a.DurationSeconds))
            .ToDictionaryAsync(i => i.Id, StringComparer.Ordinal, cancellationToken);
}
