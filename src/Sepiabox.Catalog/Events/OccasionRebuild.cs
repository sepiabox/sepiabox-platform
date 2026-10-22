using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Sepiabox.Catalog.Events;

/// <summary>What <see cref="OccasionRebuild"/> did: how many occasions and assets there are now, and what changed.</summary>
public sealed record OccasionRebuildResult(int Occasions, int Assets, int Created, int Updated, int Removed);

/// <summary>
/// <c>events rebuild</c> (S-023): groups every dated asset into occasions with <see cref="EventBuilder"/> and makes the
/// <c>events</c> and <c>event_assets</c> tables say so. Persistence only; the rules are in the builder.
/// </summary>
/// <remarks>
/// The result depends only on the assets, so a second run changes nothing. An occasion keeps the id (the filter
/// <c>eventIds</c> and the pages' links use it) of the existing occasion it shares most assets with: pairs of a new
/// grouping and an existing occasion are taken in order of shared assets (most first), then the lower id, then the
/// earlier grouping, each grouping and each id once. So the larger part of a split keeps the id; a grouping whose best
/// id went elsewhere takes its next best unclaimed one, or a new id. Its type (S-024) is kept only when its assets are
/// exactly the same, since a changed occasion may be another kind. Undated assets are in no occasion. Runs in one
/// transaction, under an advisory lock so two rebuilds never overlap; assets imported meanwhile are picked up by the
/// next run, and an asset deleted meanwhile fails the run on its foreign key and rolls it back, so running it again is
/// the fix.
/// </remarks>
public sealed class OccasionRebuild(CatalogDbContext db)
{
    /// <summary>Any constant shared by every rebuild and every classify, which change the same rows (S-023, S-024).</summary>
    public const long AdvisoryLockKey = 0x5E91AB0D;

    private const int BatchSize = 5000;

    public async Task<OccasionRebuildResult> RunAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", cancellationToken);

        var dated = await db.Assets.AsNoTracking()
            .Where(a => a.CapturedAt != null)
            .Select(a => new { a.Id, CapturedAt = a.CapturedAt!.Value, a.Location, a.PlaceCity, a.PlaceCountry })
            .ToListAsync(cancellationToken);
        var drafts = EventBuilder.Build(dated.Select(a => new EventAsset(
            a.Id, a.CapturedAt, a.Location?.Y, a.Location?.X, a.PlaceCity, a.PlaceCountry)));

        var existing = await db.Occasions.ToDictionaryAsync(o => o.Id, cancellationToken);
        var links = await db.OccasionAssets.AsNoTracking().Select(l => new { l.AssetId, l.EventId }).ToListAsync(cancellationToken);
        var eventOfAsset = links.ToDictionary(l => l.AssetId, l => l.EventId, StringComparer.Ordinal);
        var assetsOfEvent = links.GroupBy(l => l.EventId).ToDictionary(g => g.Key, g => g.Count());

        var matched = MatchIds(drafts, eventOfAsset, existing.Keys);
        var created = 0;
        var updated = 0;
        var occasions = new List<(Occasion Occasion, EventDraft Draft)>();
        for (var i = 0; i < drafts.Count; i++)
        {
            var draft = drafts[i];
            if (matched.TryGetValue(i, out var id))
            {
                // The existing occasion with most of these assets: the same occasion, with its type only if no asset came or went.
                var reused = existing[id];
                updated += Apply(reused, draft, sameAssets: assetsOfEvent.GetValueOrDefault(id) == draft.AssetIds.Count
                    && draft.AssetIds.All(asset => eventOfAsset.GetValueOrDefault(asset) == id)) ? 1 : 0;
                occasions.Add((reused, draft));
            }
            else
            {
                var occasion = new Occasion();
                Apply(occasion, draft, sameAssets: false);
                db.Occasions.Add(occasion);
                occasions.Add((occasion, draft));
                created++;
            }
        }

        // Ids are known once the new occasions are saved; the links follow.
        await db.SaveChangesAsync(cancellationToken);

        var wanted = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (occasion, draft) in occasions)
        {
            foreach (var id in draft.AssetIds)
            {
                wanted[id] = occasion.Id;
            }
        }

        // Links that moved or lost their asset first, so an asset can take its new place without breaking its key.
        var stale = eventOfAsset.Where(l => !wanted.TryGetValue(l.Key, out var now) || now != l.Value).Select(l => l.Key).ToList();
        foreach (var batch in stale.Chunk(BatchSize))
        {
            var ids = batch;
            await db.OccasionAssets.Where(l => ids.Contains(l.AssetId)).ExecuteDeleteAsync(cancellationToken);
        }

        var removed = 0;
        foreach (var gone in existing.Values.Where(o => !matched.ContainsValue(o.Id)))
        {
            db.Occasions.Remove(gone);
            removed++;
        }

        await db.SaveChangesAsync(cancellationToken);

        var missing = wanted.Where(w => !eventOfAsset.TryGetValue(w.Key, out var was) || was != w.Value).ToList();
        foreach (var batch in missing.Chunk(BatchSize))
        {
            db.OccasionAssets.AddRange(batch.Select(w => new OccasionAsset { AssetId = w.Key, EventId = w.Value }));
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        await transaction.CommitAsync(cancellationToken);
        return new OccasionRebuildResult(drafts.Count, wanted.Count, created, updated, removed);
    }

    // Which existing occasion each grouping takes the id of, by index. Every (grouping, existing occasion) pair that shares
    // assets is a candidate; the pair sharing most assets goes first, then the lower id, then the earlier grouping, and a
    // grouping or an id is used once. So the larger part of a split keeps the id, a grouping whose best id went to a larger
    // one falls back to its next-best unclaimed id, and one with none left gets a new id.
    private static Dictionary<int, long> MatchIds(
        IReadOnlyList<EventDraft> drafts, Dictionary<string, long> eventOfAsset, IEnumerable<long> existingIds)
    {
        var known = existingIds.ToHashSet();
        var pairs = new List<(int Draft, long Id, int Shared)>();
        for (var i = 0; i < drafts.Count; i++)
        {
            pairs.AddRange(drafts[i].AssetIds
                .Select(asset => eventOfAsset.GetValueOrDefault(asset))
                .Where(known.Contains)
                .GroupBy(id => id)
                .Select(g => (i, g.Key, g.Count())));
        }

        var taken = new Dictionary<int, long>();
        var used = new HashSet<long>();
        foreach (var (draft, id, _) in pairs.OrderByDescending(p => p.Shared).ThenBy(p => p.Id).ThenBy(p => p.Draft))
        {
            if (!taken.ContainsKey(draft) && used.Add(id))
            {
                taken[draft] = id;
            }
        }

        return taken;
    }

    // True when the occasion changed. The type survives only an occasion whose assets are exactly the same.
    private static bool Apply(Occasion occasion, EventDraft draft, bool sameAssets)
    {
        var centroid = draft.CentroidLatitude is { } latitude && draft.CentroidLongitude is { } longitude
            ? new Point(longitude, latitude) { SRID = 4326 }
            : null;
        var changed = occasion.StartsAt != draft.StartsAt || occasion.EndsAt != draft.EndsAt
            || occasion.PlaceLabel != draft.PlaceLabel || occasion.AssetCount != draft.AssetIds.Count
            || (occasion.Centroid?.X, occasion.Centroid?.Y) != (centroid?.X, centroid?.Y)
            || !sameAssets && occasion.Type is not null;
        if (!changed)
        {
            return false;
        }

        occasion.StartsAt = draft.StartsAt;
        occasion.EndsAt = draft.EndsAt;
        occasion.Centroid = centroid;
        occasion.PlaceLabel = draft.PlaceLabel;
        occasion.AssetCount = draft.AssetIds.Count;
        if (!sameAssets)
        {
            occasion.Type = null;
        }

        return true;
    }
}
