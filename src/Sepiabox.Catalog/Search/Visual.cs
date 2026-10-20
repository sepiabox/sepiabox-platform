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
    public static IQueryable<ScoredAsset> Rank(CatalogDbContext db, IQueryable<Asset> assets, float[] embedding, double minScore)
    {
        ArgumentNullException.ThrowIfNull(db);
        var vector = new Vector(embedding);
        var maxDistance = 1 - minScore;
        return assets
            .Join(
                db.AssetEmbeddings.Where(e => e.Model == ModelKey),
                a => a.Id,
                e => e.AssetId,
                (a, e) => new ScoredAsset { Asset = a, Distance = e.Embedding.CosineDistance(vector) })
            .Where(s => s.Distance <= maxDistance)
            .OrderBy(s => s.Distance);
    }

    /// <summary>Applies <see cref="EfSearch"/> to the current transaction; call inside one.</summary>
    public static Task PrepareAsync(CatalogDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Database.ExecuteSqlRawAsync($"SET LOCAL hnsw.ef_search = {EfSearch}", cancellationToken);
    }
}
