using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Sepiabox.Catalog.Search;

/// <summary>
/// Turns a description into a vector in the photos' embedding space (S-016). The web's implementation runs CLIP's
/// text model (<c>Sepiabox.Search</c>); the catalog only needs the vector.
/// </summary>
public interface IQueryEmbedder
{
    /// <summary>False while the text model is missing or failed to load: the UI then hides the description box.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The description's embedding, L2-normalized, with as many values as the stored embeddings; throws
    /// <see cref="VisualSearchUnavailableException"/> when the model cannot be used.
    /// </summary>
    float[] Embed(string text);
}

/// <summary>
/// Turns a region of a photo into a vector in the photos' embedding space (S-041, D-099). The web's implementation cuts
/// the region from the photo's preview and runs CLIP's image model (<c>Sepiabox.Search</c>); the catalog only needs the vector.
/// </summary>
public interface IRegionEmbedder
{
    /// <summary>False while the image model is missing or failed to load: the UI then hides the region selection.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The embedding of the part of the photo <paramref name="assetId"/> inside <paramref name="box"/>
    /// (<c>[x, y, width, height]</c>, relative), L2-normalized. Throws <see cref="AssetNotAnalysedException"/> when the
    /// catalog has no such photo or it has no preview to cut, and <see cref="VisualSearchUnavailableException"/> when the model cannot be used.
    /// </summary>
    Task<float[]> EmbedAsync(string assetId, IReadOnlyList<double> box, CancellationToken cancellationToken = default);
}

/// <summary>Search by description was asked for but the text model is not available (S-016, D-070).</summary>
public sealed class VisualSearchUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// A search for photos similar to an asset (S-017, D-086) cannot run: the asset has no stored CLIP embedding yet
/// (<see cref="Exists"/>, and <see cref="Kind"/> says whether it is a photo or a video), or the catalog has no such asset.
/// </summary>
public sealed class AssetNotAnalysedException(string assetId, string? kind)
    : Exception(kind is not null
        ? $"Asset {assetId} has not been analysed yet: it has no embedding to compare with."
        : $"No asset has the id {assetId}.")
{
    public string AssetId { get; } = assetId;

    /// <summary>The asset's kind (<c>photo</c> or <c>video</c>); null when the catalog has no such asset.</summary>
    public string? Kind { get; } = kind;

    public bool Exists => Kind is not null;
}

/// <summary>
/// One asset and its cosine distance to a description (similarity is 1 − distance). Built with property initialisers,
/// which EF Core can keep composing on, unlike a constructor.
/// </summary>
public sealed class ScoredAsset
{
    public required Asset Asset { get; init; }

    public required double Distance { get; init; }
}

/// <summary>
/// The pgvector half of search by description (D-051, D-070): cosine distance (<c>&lt;=&gt;</c>, served by the HNSW
/// index <c>ix_asset_embeddings_embedding</c> when the planner chooses it) between the stored CLIP embeddings and the
/// description's. Provider-specific, so it stays out of <see cref="SearchService"/>'s plain EF Core filters.
/// </summary>
public static class PgvectorSimilarity
{
    /// <summary>The manifests' key of the CLIP image embeddings (D-016).</summary>
    public const string ModelKey = "clip-vit-b32";

    /// <summary>
    /// The HNSW index returns at most <c>hnsw.ef_search</c> rows (40 by default), fewer than a page of results; this
    /// is the most pgvector allows, and above <see cref="SearchQuery.MaxLimit"/>, so an index scan never cuts a page.
    /// </summary>
    public const int EfSearch = 1000;

    /// <summary>
    /// <paramref name="assets"/> that have an embedding at least <paramref name="minScore"/> similar to
    /// <paramref name="embedding"/>, most similar first. Assets without an embedding never match.
    /// </summary>
    /// <param name="db">The catalog, for the embeddings.</param>
    /// <param name="assets">The candidates, already filtered.</param>
    /// <param name="embedding">The description's embedding, or a photo's own (S-017), L2-normalized.</param>
    /// <param name="minScore">The least cosine similarity kept; null keeps every asset with an embedding.</param>
    /// <param name="exact">
    /// Order by the exact distance of every candidate, never through the HNSW index. For a search with other filters:
    /// the index walk can miss matches the filters keep, even with an iterative scan, because an HNSW graph does not
    /// reach every point from the others (D-070).
    /// </param>
    public static IQueryable<ScoredAsset> Rank(CatalogDbContext db, IQueryable<Asset> assets, float[] embedding, double? minScore, bool exact = false)
    {
        ArgumentNullException.ThrowIfNull(db);
        var vector = new Vector(embedding);
        var scored = assets
            .Join(
                db.AssetEmbeddings.Where(e => e.Model == ModelKey),
                a => a.Id,
                e => e.AssetId,
                (a, e) => new ScoredAsset { Asset = a, Distance = e.Embedding.CosineDistance(vector) });
        if (minScore is { } least)
        {
            var maxDistance = 1 - least;
            scored = scored.Where(s => s.Distance <= maxDistance);
        }

        // The index serves only ORDER BY the distance expression itself; adding zero keeps the order and rules it out.
        // The id only breaks ties, so equal distances come back in the same order every time.
        return exact
            ? scored.OrderBy(s => s.Distance + 0.0).ThenBy(s => s.Asset.Id)
            : scored.OrderBy(s => s.Distance).ThenBy(s => s.Asset.Id);
    }

    /// <summary>
    /// The asset's own stored CLIP embedding, for "more photos similar to this one" (S-017): nothing is recomputed.
    /// Throws <see cref="AssetNotAnalysedException"/> when it has none, or there is no such asset.
    /// </summary>
    public static async Task<float[]> StoredEmbeddingAsync(CatalogDbContext db, string assetId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var stored = await db.AssetEmbeddings.AsNoTracking()
            .Where(e => e.AssetId == assetId && e.Model == ModelKey)
            .Select(e => e.Embedding)
            .FirstOrDefaultAsync(cancellationToken);
        return stored is not null
            ? stored.ToArray()
            : throw new AssetNotAnalysedException(
                assetId, await db.Assets.Where(a => a.Id == assetId).Select(a => a.Kind).FirstOrDefaultAsync(cancellationToken));
    }

    /// <summary>The first pgvector version with iterative index scans (<c>hnsw.iterative_scan</c>).</summary>
    public static readonly Version MinimumVersion = new(0, 8, 0);

    // The extension's version by server and database: read once, it changes only with an upgrade and a restart.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> s_versions = new(StringComparer.Ordinal);

    /// <summary>
    /// True when this catalog's pgvector can walk the HNSW index iteratively (0.8 and later), read once per database.
    /// Without it, an index walk stops after <see cref="EfSearch"/> candidates, so callers rank exactly instead.
    /// </summary>
    public static async Task<bool> CanWalkIndexAsync(CatalogDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var connection = db.Database.GetDbConnection();
        var key = connection.DataSource + "/" + connection.Database;
        if (!s_versions.TryGetValue(key, out var installed))
        {
            installed = await db.Database
                .SqlQuery<string>($"SELECT extversion AS \"Value\" FROM pg_extension WHERE extname = 'vector'")
                .SingleOrDefaultAsync(cancellationToken);
            s_versions[key] = installed;
        }

        return SupportsIterativeScan(installed);
    }

    /// <summary>
    /// Sets the index walk for the current transaction (call inside one): <see cref="EfSearch"/> candidates, and an
    /// iterative scan in strict distance order, so the threshold never cuts the walk short. Needs pgvector 0.8
    /// (<see cref="CanWalkIndexAsync"/>).
    /// </summary>
    public static Task PrepareIndexWalkAsync(CatalogDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.ExecuteSqlRawAsync(
            $"SET LOCAL hnsw.ef_search = {EfSearch}; SET LOCAL hnsw.iterative_scan = strict_order", cancellationToken);
    }

    /// <summary>True for pgvector 0.8.0 and later ("0.8.0", "0.8.1", "1.0.0").</summary>
    public static bool SupportsIterativeScan(string? extensionVersion) =>
        Version.TryParse(extensionVersion, out var version)
        && new Version(version.Major, version.Minor, Math.Max(version.Build, 0)) >= MinimumVersion;
}
