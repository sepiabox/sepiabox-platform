using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Storage;

namespace Sepiabox.Worker.Assets;

/// <summary>An asset <c>assets delete</c> would delete, with what goes with it.</summary>
/// <param name="Id">The asset's id.</param>
/// <param name="SourcePaths">Every path the catalog knows it from, in order.</param>
/// <param name="Blobs">The blobs that go, as <c>container/name</c>: its manifest, its derivatives and its original.</param>
/// <param name="OriginalBytes">The size of the original that goes (0 when none is stored).</param>
/// <param name="ManifestMissing">The manifest is already gone: an earlier run was interrupted, and this one finishes it.</param>
public sealed record MatchedAsset(string Id, IReadOnlyList<string> SourcePaths, IReadOnlyList<string> Blobs, long OriginalBytes, bool ManifestMissing);

/// <summary>An asset that was not deleted, and why; <c>assets delete</c> exits 1 for any.</summary>
public sealed record KeptAsset(string Id, IReadOnlyList<string> SourcePaths, string Reason);

/// <summary>What <c>assets delete --source</c> found: the assets it would delete and those it keeps.</summary>
public sealed record DeletionPlan(string Source, IReadOnlyList<MatchedAsset> Matched, IReadOnlyList<KeptAsset> Kept)
{
    public int BlobCount => Matched.Sum(a => a.Blobs.Count);

    public long OriginalBytes => Matched.Sum(a => a.OriginalBytes);
}

/// <param name="Plan">What was found when the run started.</param>
/// <param name="Deleted">The assets whose blobs and rows are gone.</param>
/// <param name="Kept">Assets that were kept (or whose deletion failed), with the reason; a rerun may handle them.</param>
/// <param name="ReuploadNeeded">
/// Assets whose blobs were deleted but whose manifest was written again meanwhile (an <c>upload</c> ran): they have rows
/// and a manifest but no derivatives or original, so their work folder must be uploaded again.
/// </param>
public sealed record DeletionResult(DeletionPlan Plan, int Deleted, IReadOnlyList<KeptAsset> Kept, IReadOnlyList<KeptAsset> ReuploadNeeded)
{
    public bool Clean => Kept.Count == 0 && ReuploadNeeded.Count == 0;
}

/// <summary>
/// <c>sepiabox-worker assets delete --source &lt;name&gt;</c> (S-057, D-108): removes the assets that came only from one
/// source, safely. Manifests are shared, merged state (D-036), so an asset is deleted only when every source in the catalog
/// and in its stored manifest has that name; its manifest blob goes first, with <c>If-Match</c> on the version just read,
/// so an upload that merged a second source in between wins and the asset is kept; only then its derivatives and original,
/// and last its catalog rows in one transaction. Everything is idempotent, so a rerun finishes an interrupted deletion.
/// </summary>
public sealed class AssetDeletion(CatalogDbContext db, IBlobStore store, IManifestValidator validator, ILogger logger)
{
    /// <summary>Assets looked at at once while planning.</summary>
    public const int PlanConcurrency = 8;

    /// <summary>Finds what would be deleted; changes nothing.</summary>
    public async Task<DeletionPlan> PlanAsync(string source, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var candidates = await CandidatesAsync(source, cancellationToken);
        var matched = new ConcurrentBag<MatchedAsset>();
        var kept = new ConcurrentBag<KeptAsset>();
        await Parallel.ForEachAsync(
            candidates, new ParallelOptions { MaxDegreeOfParallelism = PlanConcurrency, CancellationToken = cancellationToken },
            async (candidate, token) =>
            {
                var manifest = await store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(candidate.Id), token);
                if (Refusal(candidate, manifest, source) is { } reason)
                {
                    kept.Add(new KeptAsset(candidate.Id, candidate.SourcePaths, reason));
                    return;
                }

                matched.Add(await DescribeAsync(candidate, manifest is null, token));
            });

        return new DeletionPlan(
            source, [.. matched.OrderBy(a => a.Id, StringComparer.Ordinal)], [.. kept.OrderBy(a => a.Id, StringComparer.Ordinal)]);
    }

    /// <summary>Deletes the assets of <paramref name="plan"/>, looking at each again just before it goes.</summary>
    public async Task<DeletionResult> DeleteAsync(DeletionPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var kept = new List<KeptAsset>(plan.Kept);
        var reupload = new List<KeptAsset>();
        var deleted = 0;
        foreach (var asset in plan.Matched)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var outcome = await DeleteOneAsync(plan.Source, asset, cancellationToken);
                if (outcome.IsDeleted)
                {
                    deleted++;
                }
                else if (outcome.NeedsReupload)
                {
                    reupload.Add(new KeptAsset(asset.Id, asset.SourcePaths, "re-upload needed: its manifest was written again after its blobs were deleted"));
                }
                else
                {
                    kept.Add(new KeptAsset(asset.Id, asset.SourcePaths, outcome.Reason ?? "kept"));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && (store.IsStoreFailure(ex) || ex is DbUpdateException or InvalidOperationException))
            {
                // One asset must not stop the rest; its deletion is idempotent, so a rerun finishes it.
                Log.AssetDeleteFailed(logger, asset.Id, ex.GetType().Name);
                kept.Add(new KeptAsset(asset.Id, asset.SourcePaths, $"failed ({ex.GetType().Name}); run again"));
            }
        }

        return new DeletionResult(plan, deleted, [.. kept.OrderBy(a => a.Id, StringComparer.Ordinal)], [.. reupload]);
    }

    private readonly record struct Candidate(string Id, long SizeBytes, bool OriginalStored, IReadOnlyList<string> SourcePaths);

    private readonly record struct Outcome(bool IsDeleted, bool NeedsReupload, string? Reason)
    {
        public static readonly Outcome Deleted = new(true, false, null);

        public static readonly Outcome ReuploadNeeded = new(false, true, null);

        public static Outcome Kept(string reason) => new(false, false, reason);
    }

    // The assets whose every source in the catalog has this name; one source with another name means kept.
    private async Task<List<Candidate>> CandidatesAsync(string source, CancellationToken cancellationToken)
    {
        var rows = await db.Assets.AsNoTracking()
            .Where(a => a.Sources.Any(s => s.Name == source) && a.Sources.All(s => s.Name == source))
            .Select(a => new { a.Id, a.SizeBytes, a.OriginalStored, Paths = a.Sources.Select(s => s.Path).OrderBy(p => p).ToList() })
            .ToListAsync(cancellationToken);
        return [.. rows.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => new Candidate(r.Id, r.SizeBytes, r.OriginalStored, r.Paths))];
    }

    // Why this asset must stay, from its stored manifest; null when the manifest allows deleting it. A manifest that is
    // gone means an earlier run deleted it and was interrupted: the catalog's sources all have the name, so it is finished.
    private string? Refusal(Candidate candidate, BlobContent? manifest, string source)
    {
        if (manifest is null)
        {
            return null;
        }

        if (!validator.TryValidate(manifest.Content.ToString(), out var stored, out var problem))
        {
            return $"its manifest in storage cannot be read: {problem}";
        }

        if (stored.Id != candidate.Id)
        {
            return $"its manifest in storage describes asset {stored.Id}";
        }

        var others = stored.Sources.Select(s => s.Name).Where(n => n != source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return others.Count == 0 ? null : $"its manifest also lists the source {string.Join(", ", others.Select(n => $"'{n}'"))}";
    }

    private async Task<MatchedAsset> DescribeAsync(Candidate candidate, bool manifestMissing, CancellationToken cancellationToken)
    {
        var blobs = new List<string>();
        if (!manifestMissing)
        {
            blobs.Add($"{BlobLayout.ManifestsContainer}/{BlobLayout.ManifestBlobName(candidate.Id)}");
        }

        await foreach (var blob in store.ListAsync(BlobLayout.DerivativesContainer, candidate.Id + "/", cancellationToken))
        {
            blobs.Add($"{BlobLayout.DerivativesContainer}/{blob.Name}");
        }

        var originalBytes = 0L;
        await foreach (var blob in store.ListAsync(BlobLayout.OriginalsContainer, candidate.Id, cancellationToken))
        {
            if (blob.Name == candidate.Id)
            {
                blobs.Add($"{BlobLayout.OriginalsContainer}/{blob.Name}");
                originalBytes = candidate.SizeBytes;
            }
        }

        return new MatchedAsset(candidate.Id, candidate.SourcePaths, blobs, originalBytes, manifestMissing);
    }

    private async Task<Outcome> DeleteOneAsync(string source, MatchedAsset asset, CancellationToken cancellationToken)
    {
        // Looked at again: the plan may be minutes old, and another drive may have been uploaded since.
        var candidate = await CandidatesAsync(source, asset.Id, cancellationToken);
        if (candidate is null)
        {
            return Outcome.Kept("no longer matches: its catalog rows changed since the plan");
        }

        var name = BlobLayout.ManifestBlobName(asset.Id);
        var manifest = await store.ReadAsync(BlobLayout.ManifestsContainer, name, cancellationToken);
        if (Refusal(candidate.Value, manifest, source) is { } reason)
        {
            return Outcome.Kept(reason);
        }

        // 2. The manifest first, only if nobody wrote it since it was read: what any upload would merge into is gone, or
        // the delete loses its condition and the asset stays whole.
        if (manifest is not null
            && await store.DeleteIfVersionAsync(BlobLayout.ManifestsContainer, name, manifest.Version, cancellationToken) == BlobDeleteStatus.ConditionNotMet)
        {
            return Outcome.Kept("its manifest changed while it was being deleted (an upload?); run again");
        }

        // 3. Then what the manifest described; a blob that is already gone counts as deleted.
        await foreach (var blob in store.ListAsync(BlobLayout.DerivativesContainer, asset.Id + "/", cancellationToken))
        {
            await store.DeleteAsync(BlobLayout.DerivativesContainer, blob.Name, cancellationToken);
        }

        await store.DeleteAsync(BlobLayout.OriginalsContainer, asset.Id, cancellationToken);

        // A manifest that came back means an upload ran meanwhile and its blobs are now gone: its rows stay, and it must be uploaded again.
        if (await store.ReadAsync(BlobLayout.ManifestsContainer, name, cancellationToken) is not null)
        {
            return Outcome.ReuploadNeeded;
        }

        // 4. The catalog rows, all or none.
        await DeleteRowsAsync(asset.Id, name, cancellationToken);
        return Outcome.Deleted;
    }

    private async Task<Candidate?> CandidatesAsync(string source, string id, CancellationToken cancellationToken)
    {
        var rows = await db.Assets.AsNoTracking()
            .Where(a => a.Id == id && a.Sources.Any(s => s.Name == source) && a.Sources.All(s => s.Name == source))
            .Select(a => new { a.Id, a.SizeBytes, a.OriginalStored, Paths = a.Sources.Select(s => s.Path).OrderBy(p => p).ToList() })
            .SingleOrDefaultAsync(cancellationToken);
        return rows is null ? null : new Candidate(rows.Id, rows.SizeBytes, rows.OriginalStored, rows.Paths);
    }

    private async Task DeleteRowsAsync(string id, string manifestBlobName, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AssetEmbeddings.Where(e => e.AssetId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Faces.Where(f => f.AssetId == id).ExecuteDeleteAsync(cancellationToken);
        await db.ErasedFaces.Where(f => f.AssetId == id).ExecuteDeleteAsync(cancellationToken);
        await db.OccasionAssets.Where(l => l.AssetId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Derivatives.Where(d => d.AssetId == id).ExecuteDeleteAsync(cancellationToken);
        await db.AssetSources.Where(s => s.AssetId == id).ExecuteDeleteAsync(cancellationToken);
        await db.ManifestImports.Where(m => m.BlobName == manifestBlobName).ExecuteDeleteAsync(cancellationToken);
        await db.Assets.Where(a => a.Id == id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
