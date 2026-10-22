using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;
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
/// <param name="Source">The source name asked for.</param>
/// <param name="Matched">The assets that would be deleted.</param>
/// <param name="Kept">The assets that stay, with the reason.</param>
/// <param name="SourceTypes">The types of the matched assets' sources (<c>local</c>, <c>google-takeout</c>, ...), so a wrong source name shows.</param>
public sealed record DeletionPlan(string Source, IReadOnlyList<MatchedAsset> Matched, IReadOnlyList<KeptAsset> Kept, IReadOnlyList<string> SourceTypes)
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

    /// <summary>Assets deleted at once: each takes about twenty storage round trips, so a large source would take hours one by one.</summary>
    public const int DeleteConcurrency = 8;

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

        var matchedIds = matched.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var types = candidates.Where(c => matchedIds.Contains(c.Id)).SelectMany(c => c.Types).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return new DeletionPlan(
            source, [.. matched.OrderBy(a => a.Id, StringComparer.Ordinal)], [.. kept.OrderBy(a => a.Id, StringComparer.Ordinal)], types);
    }

    /// <summary>Deletes the assets of <paramref name="plan"/>, looking at each again just before it goes.</summary>
    public async Task<DeletionResult> DeleteAsync(DeletionPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var kept = new ConcurrentBag<KeptAsset>(plan.Kept);
        var reupload = new ConcurrentBag<KeptAsset>();
        var deleted = 0;
        // One database context is used by one task at a time; the storage calls, which are most of the time, run side by side.
        using var database = new SemaphoreSlim(1, 1);
        await Parallel.ForEachAsync(
            plan.Matched, new ParallelOptions { MaxDegreeOfParallelism = DeleteConcurrency, CancellationToken = cancellationToken },
            async (asset, token) =>
            {
                try
                {
                    var outcome = await DeleteOneAsync(plan.Source, asset, database, token);
                    if (outcome.IsDeleted)
                    {
                        Interlocked.Increment(ref deleted);
                    }
                    else if (outcome.NeedsReupload)
                    {
                        reupload.Add(new KeptAsset(asset.Id, asset.SourcePaths, outcome.Reason ?? "re-upload needed"));
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
                    if (ex is InvalidOperationException)
                    {
                        // Not a storage or database failure: a bug (a threading one, say) must not hide behind "run again".
                        Log.AssetDeleteInvalid(logger, asset.Id, ex.Message);
                    }

                    kept.Add(new KeptAsset(asset.Id, asset.SourcePaths, $"failed ({ex.GetType().Name}); run again"));
                }
            });

        return new DeletionResult(plan, deleted, [.. kept.OrderBy(a => a.Id, StringComparer.Ordinal)], [.. reupload.OrderBy(a => a.Id, StringComparer.Ordinal)]);
    }

    private readonly record struct Candidate(string Id, long SizeBytes, bool OriginalStored, IReadOnlyList<string> SourcePaths, IReadOnlyList<string> Types);

    private readonly record struct Outcome(bool IsDeleted, bool NeedsReupload, string? Reason)
    {
        public static readonly Outcome Deleted = new(true, false, null);

        public static Outcome ReuploadNeeded(string reason) => new(false, true, reason);

        public static Outcome Kept(string reason) => new(false, false, reason);
    }

    // The assets whose every source in the catalog has this name; one source with another name means kept.
    private async Task<List<Candidate>> CandidatesAsync(string source, CancellationToken cancellationToken)
    {
        var rows = await db.Assets.AsNoTracking()
            .Where(a => a.Sources.Any(s => s.Name == source) && a.Sources.All(s => s.Name == source))
            .Select(a => new { a.Id, a.SizeBytes, a.OriginalStored, Paths = a.Sources.Select(s => s.Path).OrderBy(p => p).ToList(), Types = a.Sources.Select(s => s.Type).Distinct().ToList() })
            .ToListAsync(cancellationToken);
        return [.. rows.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => new Candidate(r.Id, r.SizeBytes, r.OriginalStored, r.Paths, r.Types))];
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

    private async Task<Outcome> DeleteOneAsync(string source, MatchedAsset asset, SemaphoreSlim database, CancellationToken cancellationToken)
    {
        // Looked at again: the plan may be minutes old, and another drive may have been uploaded since.
        Candidate? candidate;
        await database.WaitAsync(cancellationToken);
        try
        {
            candidate = await CandidatesAsync(source, asset.Id, cancellationToken);
        }
        finally
        {
            database.Release();
        }

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
        if (await store.ReadAsync(BlobLayout.ManifestsContainer, name, cancellationToken) is { } returned)
        {
            return Outcome.ReuploadNeeded(ReuploadReason(returned, source));
        }

        // 4. The catalog rows, all or none.
        await database.WaitAsync(cancellationToken);
        try
        {
            await DeleteRowsAsync(asset.Id, name, cancellationToken);
        }
        finally
        {
            database.Release();
        }

        return Outcome.Deleted;
    }

    // A manifest that came back is usually another source's upload of the same photo (the one it merged), whose work folder
    // is the one to send again, not the deleted source's.
    private string ReuploadReason(BlobContent manifest, string source)
    {
        var others = validator.TryValidate(manifest.Content.ToString(), out var stored, out _)
            ? stored.Sources.Select(s => s.Name).Where(n => n != source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()
            : [];
        return others.Count > 0
            ? $"re-upload needed: its manifest was written again after its blobs were deleted, by an upload of {string.Join(", ", others.Select(n => $"'{n}'"))}; "
                + $"run upload with {string.Join(" and ", others.Select(n => $"--resend-source \"{n}\""))} on that work folder"
            : "re-upload needed: its manifest was written again after its blobs were deleted; upload the work folder that wrote it again with --resend-source";
    }

    private async Task<Candidate?> CandidatesAsync(string source, string id, CancellationToken cancellationToken)
    {
        var rows = await db.Assets.AsNoTracking()
            .Where(a => a.Id == id && a.Sources.Any(s => s.Name == source) && a.Sources.All(s => s.Name == source))
            .Select(a => new { a.Id, a.SizeBytes, a.OriginalStored, Paths = a.Sources.Select(s => s.Path).OrderBy(p => p).ToList(), Types = a.Sources.Select(s => s.Type).Distinct().ToList() })
            .SingleOrDefaultAsync(cancellationToken);
        return rows is null ? null : new Candidate(rows.Id, rows.SizeBytes, rows.OriginalStored, rows.Paths, rows.Types);
    }

    private async Task DeleteRowsAsync(string id, string manifestBlobName, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // The people's stored count and centroid (an average of face embeddings, biometric data) are recomputed from the faces
        // that remain, under the lock the importers and the family's edits take (D-077), so no trace of a deleted photo's faces is left.
        await FaceAssigner.LockAsync(db, cancellationToken);
        // Under the lock, nothing tracked from an earlier asset is trusted: a person loaded then may have been named since, and a
        // stale unnamed copy would be removed by the recompute.
        db.ChangeTracker.Clear();
        var personIds = await db.Faces.Where(f => f.AssetId == id && f.PersonId != null).Select(f => f.PersonId!.Value).Distinct().ToListAsync(cancellationToken);
        await db.AssetEmbeddings.Where(e => e.AssetId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Faces.Where(f => f.AssetId == id).ExecuteDeleteAsync(cancellationToken);
        if (personIds.Count > 0)
        {
            var assigner = new FaceAssigner(db);
            foreach (var person in await db.People.Where(p => personIds.Contains(p.Id)).ToListAsync(cancellationToken))
            {
                // Removes an unnamed person with no face left, keeps a named one with a count of 0 and no centroid.
                await assigner.RecomputeAsync(person, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await db.ErasedFaces.Where(f => f.AssetId == id).ExecuteDeleteAsync(cancellationToken);
        await db.OccasionAssets.Where(l => l.AssetId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Derivatives.Where(d => d.AssetId == id).ExecuteDeleteAsync(cancellationToken);
        await db.AssetSources.Where(s => s.AssetId == id).ExecuteDeleteAsync(cancellationToken);
        await db.ManifestImports.Where(m => m.BlobName == manifestBlobName).ExecuteDeleteAsync(cancellationToken);
        await db.Assets.Where(a => a.Id == id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
