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

/// <summary>Search by description was asked for but the text model is not available (S-016, D-070).</summary>
public sealed class VisualSearchUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

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
    /// <param name="embedding">The description's embedding, L2-normalized.</param>
    /// <param name="minScore">The least cosine similarity kept.</param>
    /// <param name="exact">
    /// Order by the exact distance of every candidate, never through the HNSW index. For a search with other filters:
    /// the index walk can miss matches the filters keep, even with an iterative scan, because an HNSW graph does not
    /// reach every point from the others (D-070).
    /// </param>
    public static IQueryable<ScoredAsset> Rank(CatalogDbContext db, IQueryable<Asset> assets, float[] embedding, double minScore, bool exact = false)
    {
        ArgumentNullException.ThrowIfNull(db);
        var vector = new Vector(embedding);
        var maxDistance = 1 - minScore;
        var scored = assets
            .Join(
                db.AssetEmbeddings.Where(e => e.Model == ModelKey),
                a => a.Id,
                e => e.AssetId,
                (a, e) => new ScoredAsset { Asset = a, Distance = e.Embedding.CosineDistance(vector) })
            .Where(s => s.Distance <= maxDistance);
        // The index serves only ORDER BY the distance expression itself; adding zero keeps the order and rules it out.
        // The id only breaks ties, so equal distances come back in the same order every time.
        return exact
            ? scored.OrderBy(s => s.Distance + 0.0).ThenBy(s => s.Asset.Id)
            : scored.OrderBy(s => s.Distance).ThenBy(s => s.Asset.Id);
    }

    /// <summary>The first pgvector version with iterative index scans (<c>hnsw.iterative_scan</c>).</summary>
    public static readonly Version MinimumVersion = new(0, 8, 0);

    /// <summary>
    /// Sets the index scan for the current transaction (call inside one): <see cref="EfSearch"/> candidates, and an
    /// iterative scan in strict distance order, so filters applied after the index (a year, a city) still find every
    /// match instead of only those among the first candidates. Before pgvector 0.8 there is no iterative scan, and a
    /// filtered search could silently miss photos: it is refused with <see cref="VisualSearchUnavailableException"/>.
    /// </summary>
    public static async Task PrepareAsync(CatalogDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var installed = await db.Database
            .SqlQuery<string>($"SELECT extversion AS \"Value\" FROM pg_extension WHERE extname = 'vector'")
            .SingleOrDefaultAsync(cancellationToken);
        if (!SupportsIterativeScan(installed))
        {
            throw new VisualSearchUnavailableException(
                $"Search by description needs pgvector {MinimumVersion} or later; the catalog has {installed ?? "none"}.");
        }

        await db.Database.ExecuteSqlRawAsync(
            $"SET LOCAL hnsw.ef_search = {EfSearch}; SET LOCAL hnsw.iterative_scan = strict_order", cancellationToken);
    }

    /// <summary>True for pgvector 0.8.0 and later ("0.8.0", "0.8.1", "1.0.0").</summary>
    public static bool SupportsIterativeScan(string? extensionVersion) =>
        Version.TryParse(extensionVersion, out var version)
        && new Version(version.Major, version.Minor, Math.Max(version.Build, 0)) >= MinimumVersion;
}
