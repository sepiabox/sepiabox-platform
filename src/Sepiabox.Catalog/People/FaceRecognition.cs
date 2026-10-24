using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Sepiabox.Catalog.People;

/// <summary>
/// The library's face recognition switch (S-022, D-081), read from <c>settings</c>.
/// </summary>
/// <param name="On">Whether imports store faces. On unless the family turned it off.</param>
/// <param name="ErasedAt">When the family last turned it off: faces found at or before it are never imported again.</param>
/// <param name="SweptThrough">
/// Every manifest blob last written before this time carries the erase (the <c>faces sweep</c> job); null until its
/// first full pass after <paramref name="ErasedAt"/>.
/// </param>
/// <param name="EnabledAt">When the family last turned it back on after an erase (server time).</param>
public sealed record FaceRecognitionState(bool On, DateTimeOffset? ErasedAt, DateTimeOffset? SweptThrough, DateTimeOffset? EnabledAt = null)
{
    public static readonly FaceRecognitionState Default = new(true, null, null);

    /// <summary>
    /// How far a computer's clock, which writes <c>facesDetectedAt</c>, may lag the server's when faces are compared with
    /// <see cref="EnabledAt"/> (D-081); <c>upload</c> refuses a clock more than 5 minutes off.
    /// </summary>
    public static readonly TimeSpan ClockMargin = TimeSpan.FromMinutes(15);

    /// <summary>Faces were erased and the stored manifests do not all carry the erase yet.</summary>
    public bool ManifestsPending => ErasedAt is not null && SweptThrough is null;

    /// <summary>
    /// Whether an import stores faces found at <paramref name="detectedAt"/>: only while recognition is on, and only
    /// faces found after the last erase (faces without a time were found before the field existed, so before it).
    /// </summary>
    public bool Accepts(DateTimeOffset? detectedAt) =>
        On
        && (ErasedAt is not { } erased || (detectedAt is { } detected && detected > erased))
        // Found while recognition was off, give or take a computer's clock: not imported either.
        && (EnabledAt is not { } enabled || (detectedAt is { } found && found > enabled - ClockMargin));
}

/// <summary>What "Ignore the unnamed people with few faces" did; <paramref name="Refused"/> when the number of people was not the one confirmed.</summary>
public sealed record IgnoreResult(int Faces, int People, bool Refused = false);

/// <summary>
/// Turning face recognition off and on, and deleting one person's face data (S-022, D-081). Each change is one
/// transaction under the people lock (<see cref="FaceAssigner.LockAsync"/>, D-077), so an import either finished
/// before it or sees the new state; and each deletion leaves a <see cref="FaceDataDeletion"/> record.
/// </summary>
public sealed class FaceRecognition(CatalogDbContext db, TimeProvider clock)
{
    public const string RecognitionKey = "faces.recognition";
    public const string ErasedAtKey = "faces.erased_at";
    public const string SweptThroughKey = "faces.swept_through";
    public const string EnabledAtKey = "faces.enabled_at";
    public const string SweepKey = "faces.sweep";

    public static async Task<FaceRecognitionState> ReadAsync(CatalogDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var values = await db.Settings.AsNoTracking()
            .Where(s => s.Key == RecognitionKey || s.Key == ErasedAtKey || s.Key == SweptThroughKey || s.Key == EnabledAtKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        return new FaceRecognitionState(
            !values.TryGetValue(RecognitionKey, out var on) || on != "off",
            Time(values.GetValueOrDefault(ErasedAtKey)),
            Time(values.GetValueOrDefault(SweptThroughKey)),
            Time(values.GetValueOrDefault(EnabledAtKey)));
    }

    /// <summary>
    /// Turns recognition off: deletes every face, person (with their centroids and names), erased-face box and asset
    /// face version, and records when, so imports store no face found until then and the <c>faces sweep</c> job erases
    /// them from the stored manifests. Null, changing and recording nothing, when it is already off with no face and no
    /// person: a second click is not a second erase.
    /// </summary>
    public Task<FaceDataDeletion?> TurnOffAsync(string deletedBy, CancellationToken cancellationToken = default)
    {
        var by = NormalizeEmail(deletedBy);
        return InTransactionAsync<FaceDataDeletion?>(async () =>
        {
            if (!(await ReadAsync(db, cancellationToken)).On
                && !await db.Faces.AnyAsync(cancellationToken) && !await db.People.AnyAsync(cancellationToken))
            {
                return null;
            }

            var now = Seconds(clock.GetUtcNow());
            var deletion = new FaceDataDeletion
            {
                DeletedAt = now,
                DeletedBy = by,
                Scope = FaceDataDeletionScopes.Library,
                Faces = await db.Faces.ExecuteDeleteAsync(cancellationToken),
                People = await db.People.ExecuteDeleteAsync(cancellationToken),
            };
            await db.ErasedFaces.ExecuteDeleteAsync(cancellationToken);
            await db.Assets.Where(a => a.FacesVersion != null)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.FacesVersion, (int?)null), cancellationToken);
            await SetAsync(RecognitionKey, "off", cancellationToken);
            await SetAsync(ErasedAtKey, Format(now), cancellationToken);
            await db.Settings.Where(s => s.Key == SweptThroughKey || s.Key == SweepKey).ExecuteDeleteAsync(cancellationToken);
            db.FaceDataDeletions.Add(deletion);
            await db.SaveChangesAsync(cancellationToken);
            return deletion;
        }, cancellationToken);
    }

    /// <summary>
    /// Turns recognition back on. Refused (false) while the stored manifests do not all carry the last erase yet, so
    /// faces found from now on never meet a manifest that still has the old ones.
    /// </summary>
    public Task<bool> TurnOnAsync(CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            if ((await ReadAsync(db, cancellationToken)).ManifestsPending)
            {
                return false;
            }

            await SetAsync(RecognitionKey, "on", cancellationToken);
            await SetAsync(EnabledAtKey, Format(Seconds(clock.GetUtcNow())), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    /// <summary>
    /// "Delete face data" on a person: deletes the person and every face row pointing to them, and remembers the boxes
    /// of their own faces (never the embeddings) so a re-import does not bring them back. The photos stay. Null when the
    /// person is not in the catalog.
    /// </summary>
    public Task<FaceDataDeletion?> DeletePersonAsync(long personId, string deletedBy, CancellationToken cancellationToken = default) =>
        DeleteOnePersonAsync(personId, deletedBy, ignoring: false, cancellationToken);

    /// <summary>
    /// "Ignore this person" (#107, D-117): the same erase as <see cref="DeletePersonAsync"/> for a stranger (a poster, a
    /// celebrity, a face in a crowd), but only while the person is unnamed, and with every face row's box remembered, so
    /// none comes back as a new person. Null when the person is not there or has a name or a link to a user.
    /// </summary>
    public Task<FaceDataDeletion?> IgnorePersonAsync(long personId, string deletedBy, CancellationToken cancellationToken = default) =>
        DeleteOnePersonAsync(personId, deletedBy, ignoring: true, cancellationToken);

    private Task<FaceDataDeletion?> DeleteOnePersonAsync(long personId, string deletedBy, bool ignoring, CancellationToken cancellationToken)
    {
        var by = NormalizeEmail(deletedBy);
        return InTransactionAsync<FaceDataDeletion?>(async () =>
        {
            if (await db.People.FindAsync([personId], cancellationToken) is not { } person || (ignoring && !IsUnnamed(person)))
            {
                return null;
            }

            var now = Seconds(clock.GetUtcNow());
            var faces = await db.Faces.Where(f => f.PersonId == personId).ToListAsync(cancellationToken);
            // Excluded faces and "not a face" were never this person's: deleted with them, and found again as new faces by a
            // later import (unless the person is being ignored, when every box is remembered).
            db.ErasedFaces.AddRange(faces.Where(f => ignoring || FaceAssignments.Counts(f.Assignment))
                .Select(f => new ErasedFace { AssetId = f.AssetId, Box = f.Box, ErasedAt = now }));
            db.Faces.RemoveRange(faces);
            db.People.Remove(person);
            var deletion = new FaceDataDeletion
            {
                DeletedAt = now,
                DeletedBy = by,
                Scope = ignoring ? FaceDataDeletionScopes.Ignore : FaceDataDeletionScopes.Person,
                Faces = faces.Count,
                People = 1,
            };
            db.FaceDataDeletions.Add(deletion);
            await db.SaveChangesAsync(cancellationToken);
            return deletion;
        }, cancellationToken);
    }

    /// <summary>The most faces a person may have to be offered in "Ignore the unnamed people with few faces".</summary>
    public const int FewFaces = 2;

    /// <summary>Whether a person has neither a name nor a link to a user: the only people ever ignored (#107).</summary>
    public static bool IsUnnamed(Person person) => person.Name is null && person.UserEmail is null;

    /// <summary>The unnamed people with at least one and at most <paramref name="maxFaces"/> face rows, as the bulk action sees them.</summary>
    public static IQueryable<Person> UnnamedWithFewFaces(CatalogDbContext db, int maxFaces)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.People.Where(p => p.Name == null && p.UserEmail == null
            && db.Faces.Count(f => f.PersonId == p.Id) >= 1 && db.Faces.Count(f => f.PersonId == p.Id) <= maxFaces);
    }

    /// <summary>
    /// "Ignore this face" and "Ignore the unnamed faces in this photo" (#107, D-117): deletes the faces of
    /// <paramref name="assetId"/> that belong to an unnamed person (just <paramref name="faceId"/> when it is given) and
    /// remembers their boxes, as <see cref="DeletePersonAsync"/> does, so a re-import does not find them again. Each person
    /// they belonged to has their count and centroid recomputed, and an unnamed one left with no face is removed. A face of
    /// a named person is never touched. Null when there was nothing to ignore.
    /// </summary>
    public Task<FaceDataDeletion?> IgnoreFacesAsync(string assetId, long? faceId, string deletedBy, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        var by = NormalizeEmail(deletedBy);
        return InTransactionAsync<FaceDataDeletion?>(async () =>
        {
            var faces = await db.Faces
                .Where(f => f.AssetId == assetId && (faceId == null || f.Id == faceId) && f.Person != null && f.Person.Name == null && f.Person.UserEmail == null)
                .ToListAsync(cancellationToken);
            if (faces.Count == 0)
            {
                return null;
            }

            var now = Seconds(clock.GetUtcNow());
            db.ErasedFaces.AddRange(faces.Select(f => new ErasedFace { AssetId = f.AssetId, Box = f.Box, ErasedAt = now }));
            db.Faces.RemoveRange(faces);
            await db.SaveChangesAsync(cancellationToken);
            var removed = await RecomputeAsync(faces.Select(f => f.PersonId!.Value).Distinct().ToList(), cancellationToken);
            var deletion = new FaceDataDeletion
            {
                DeletedAt = now,
                DeletedBy = by,
                Scope = FaceDataDeletionScopes.Ignore,
                Faces = faces.Count,
                People = removed,
            };
            db.FaceDataDeletions.Add(deletion);
            await db.SaveChangesAsync(cancellationToken);
            return deletion;
        }, cancellationToken);
    }

    /// <summary>
    /// "Ignore the unnamed people with few faces" (#107, D-117): deletes every unnamed person with at most
    /// <paramref name="maxFaces"/> faces and those faces, remembering their boxes. Nothing is changed (Refused) unless exactly
    /// <paramref name="expected"/> people match, the number the person saw when they confirmed. A named person, or one linked to a user,
    /// is never touched.
    /// </summary>
    public Task<IgnoreResult> IgnoreUnnamedPeopleAsync(int maxFaces, int expected, string deletedBy, CancellationToken cancellationToken = default)
    {
        var by = NormalizeEmail(deletedBy);
        return InTransactionAsync(async () =>
        {
            var people = await UnnamedWithFewFaces(db, maxFaces).ToListAsync(cancellationToken);
            if (people.Count != expected || people.Count == 0)
            {
                return new IgnoreResult(0, 0, Refused: true);
            }

            var ids = people.Select(p => p.Id).ToList();
            var faces = await db.Faces.Where(f => f.PersonId != null && ids.Contains(f.PersonId.Value)).ToListAsync(cancellationToken);
            var now = Seconds(clock.GetUtcNow());
            db.ErasedFaces.AddRange(faces.Select(f => new ErasedFace { AssetId = f.AssetId, Box = f.Box, ErasedAt = now }));
            db.Faces.RemoveRange(faces);
            db.People.RemoveRange(people);
            db.FaceDataDeletions.Add(new FaceDataDeletion
            {
                DeletedAt = now,
                DeletedBy = by,
                Scope = FaceDataDeletionScopes.Ignore,
                Faces = faces.Count,
                People = people.Count,
            });
            await db.SaveChangesAsync(cancellationToken);
            return new IgnoreResult(faces.Count, people.Count);
        }, cancellationToken);
    }

    // The people's count and centroid again from the faces that are left; an unnamed one with none goes. How many went.
    private async Task<int> RecomputeAsync(IReadOnlyList<long> personIds, CancellationToken cancellationToken)
    {
        var assigner = new FaceAssigner(db);
        var removed = 0;
        foreach (var person in await db.People.Where(p => personIds.Contains(p.Id)).ToListAsync(cancellationToken))
        {
            await assigner.RecomputeAsync(person, cancellationToken);
            if (db.Entry(person).State == EntityState.Deleted)
            {
                removed++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return removed;
    }

    /// <summary>
    /// Records that every manifest blob last written before <paramref name="through"/> carries the erase of
    /// <paramref name="erasedAt"/>. Ignored (false) when recognition was turned off again since: that erase needs its
    /// own full pass.
    /// </summary>
    public Task<bool> MarkSweptAsync(DateTimeOffset erasedAt, DateTimeOffset through, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            if ((await ReadAsync(db, cancellationToken)).ErasedAt != erasedAt)
            {
                return false;
            }

            await SetAsync(SweptThroughKey, Format(through), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    /// <summary>Where an unfinished sweep of the current erase stopped, if any (null otherwise).</summary>
    public static async Task<SweepCheckpoint?> ReadSweepAsync(CatalogDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var value = await db.Settings.AsNoTracking().Where(s => s.Key == SweepKey).Select(s => s.Value).SingleOrDefaultAsync(cancellationToken);
        var state = await ReadAsync(db, cancellationToken);
        try
        {
            return value is not null && JsonSerializer.Deserialize<SweepCheckpoint>(value) is { } checkpoint && checkpoint.ErasedAt == state.ErasedAt
                ? checkpoint
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Saves where the sweep is (false, saving nothing, when recognition was turned off again since: that erase starts a
    /// sweep of its own); null clears it.
    /// </summary>
    public Task<bool> SaveSweepAsync(DateTimeOffset erasedAt, SweepCheckpoint? checkpoint, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            if ((await ReadAsync(db, cancellationToken)).ErasedAt != erasedAt)
            {
                return false;
            }

            if (checkpoint is null)
            {
                await db.Settings.Where(s => s.Key == SweepKey).ExecuteDeleteAsync(cancellationToken);
            }
            else
            {
                await SetAsync(SweepKey, JsonSerializer.Serialize(checkpoint), cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }

            return true;
        }, cancellationToken);

    /// <summary>The last deletions, newest first, for the settings page.</summary>
    public static async Task<IReadOnlyList<FaceDataDeletion>> HistoryAsync(CatalogDbContext db, int count, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        return await db.FaceDataDeletions.AsNoTracking()
            .OrderByDescending(d => d.DeletedAt).ThenByDescending(d => d.Id)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    private async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        if (await db.Settings.FindAsync([key], cancellationToken) is { } setting)
        {
            setting.Value = value;
        }
        else
        {
            db.Settings.Add(new Setting { Key = key, Value = value });
        }
    }

    // Whole seconds, UTC: compared with the facesDetectedAt that process writes, which has no fractions either.
    private static DateTimeOffset Seconds(DateTimeOffset time) =>
        new(time.UtcTicks - (time.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    private static string Format(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? Time(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time : null;

    private static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return email.Trim().ToLowerInvariant();
    }

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> change, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await FaceAssigner.LockAsync(db, cancellationToken);
            var result = await change();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}

/// <summary>
/// An unfinished <c>faces sweep</c> (D-081): the erase it carries, the blobs it looks at (written since
/// <paramref name="Since"/>, all when null), when it started, the listing's continuation token after the last page
/// done, and whether a manifest of an earlier page could not be erased (then the pass cannot record itself as complete).
/// </summary>
public sealed record SweepCheckpoint(DateTimeOffset ErasedAt, DateTimeOffset? Since, DateTimeOffset StartedAt, string? Token, bool Failed);
