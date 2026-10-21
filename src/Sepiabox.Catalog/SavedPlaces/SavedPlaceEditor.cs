using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;
using Sepiabox.Catalog.People;

namespace Sepiabox.Catalog.SavedPlaces;

/// <summary>What became of a change to the family's places (S-014).</summary>
public enum SavedPlaceOutcome
{
    Done,

    /// <summary>The place is not in the catalog any more.</summary>
    NotFound,

    /// <summary>Another place already has that name, ignoring case.</summary>
    NameTaken,

    /// <summary>The name is blank, longer than <see cref="SavedPlaceEditor.MaxNameLength"/> or has control characters.</summary>
    NameInvalid,

    /// <summary>The latitude or longitude is not a point on Earth.</summary>
    LocationInvalid,

    /// <summary>The radius is outside <see cref="SavedPlaceEditor.MinRadiusM"/>..<see cref="SavedPlaceEditor.MaxRadiusM"/>.</summary>
    RadiusInvalid,
}

/// <summary>
/// The family's places (S-014, D-083): create, change and delete. Each change is one transaction that first takes a lock
/// of its own, so two family members marking two homes at once end with one; the unique indexes on <c>lower(name)</c> and
/// on the home only back the checks up. Deleting a place never touches an asset: places are only ever compared with the
/// assets' locations when searching.
/// </summary>
public sealed class SavedPlaceEditor(CatalogDbContext db)
{
    public const int MinRadiusM = 10;
    public const int MaxRadiusM = 50_000;
    public const int DefaultRadiusM = 200;

    /// <summary>
    /// The longest name: a place's name goes to the question translator (D-083), so it is a short label such as "Casa da
    /// praia", never a street address.
    /// </summary>
    public const int MaxNameLength = 60;

    /// <summary>The unique index on <c>lower(name)</c>, added by the migration <c>SavedPlaces</c>.</summary>
    public const string NameIndex = "ix_saved_places_lower_name";

    /// <summary>The advisory lock every change to the places takes, so the home moves atomically.</summary>
    public const long PlacesLockKey = 0x5E91AB21;

    /// <summary>Creates a place; with <paramref name="isHome"/>, it becomes the home and the previous home loses the mark.</summary>
    public Task<(SavedPlaceOutcome Outcome, long? Id)> CreateAsync(
        string? name, double latitude, double longitude, int radiusM, bool isHome, CancellationToken cancellationToken = default)
    {
        if (Check(name, latitude, longitude, radiusM, out var clean) is { } refused)
        {
            return Task.FromResult<(SavedPlaceOutcome, long?)>((refused, null));
        }

        return InTransactionAsync<(SavedPlaceOutcome, long?)>(async () =>
        {
            if (await NameTakenAsync(clean, null, cancellationToken))
            {
                return (SavedPlaceOutcome.NameTaken, null);
            }

            if (isHome)
            {
                await ClearHomeAsync(cancellationToken);
            }

            var place = new SavedPlace { Name = clean, Location = PointAt(latitude, longitude), RadiusM = radiusM, IsHome = isHome };
            db.SavedPlaces.Add(place);
            await db.SaveChangesAsync(cancellationToken);
            return (SavedPlaceOutcome.Done, place.Id);
        }, cancellationToken);
    }

    /// <summary>
    /// Renames the place, changes its radius (null keeps it) and whether it is the home (marking it moves the mark here).
    /// A place is not moved: it is deleted and saved again.
    /// </summary>
    public Task<SavedPlaceOutcome> UpdateAsync(long id, string? name, int? radiusM, bool isHome, CancellationToken cancellationToken = default)
    {
        if (Check(name, 0, 0, radiusM ?? DefaultRadiusM, out var clean) is { } refused)
        {
            return Task.FromResult(refused);
        }

        return InTransactionAsync(async () =>
        {
            if (await db.SavedPlaces.FindAsync([id], cancellationToken) is not { } place)
            {
                return SavedPlaceOutcome.NotFound;
            }

            if (await NameTakenAsync(clean, id, cancellationToken))
            {
                return SavedPlaceOutcome.NameTaken;
            }

            if (isHome && !place.IsHome)
            {
                await ClearHomeAsync(cancellationToken);
            }

            place.Name = clean;
            place.RadiusM = radiusM ?? place.RadiusM;
            place.IsHome = isHome;
            await db.SaveChangesAsync(cancellationToken);
            return SavedPlaceOutcome.Done;
        }, cancellationToken);
    }

    /// <summary>Deletes the place; the assets taken there stay as they are.</summary>
    public Task<SavedPlaceOutcome> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
            await db.SavedPlaces.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken) == 0
                ? SavedPlaceOutcome.NotFound
                : SavedPlaceOutcome.Done,
            cancellationToken);

    /// <summary>A WGS 84 point for the geography column: X is longitude, Y is latitude.</summary>
    public static Point PointAt(double latitude, double longitude) => new(longitude, latitude) { SRID = 4326 };

    // The name cleaned as people's are (D-079): NFC, invisible formatting dropped, white space collapsed.
    private static SavedPlaceOutcome? Check(string? name, double latitude, double longitude, int radiusM, out string clean)
    {
        clean = string.Empty;
        if (PeopleCuration.CleanName(name, out var cleaned) is not null || cleaned is null || cleaned.Length > MaxNameLength)
        {
            return SavedPlaceOutcome.NameInvalid;
        }

        clean = cleaned;
        if (!(latitude is >= -90 and <= 90) || !(longitude is >= -180 and <= 180))
        {
            return SavedPlaceOutcome.LocationInvalid;
        }

        return radiusM is < MinRadiusM or > MaxRadiusM ? SavedPlaceOutcome.RadiusInvalid : null;
    }

    // lower() in SQL on both sides, exactly as the unique index compares them.
    private Task<bool> NameTakenAsync(string name, long? except, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<bool>(
            $"SELECT EXISTS (SELECT 1 FROM saved_places WHERE lower(name) = lower({name}) AND id <> {except ?? 0}) AS \"Value\"")
            .SingleAsync(cancellationToken);

    private Task<int> ClearHomeAsync(CancellationToken cancellationToken) =>
        db.SavedPlaces.Where(p => p.IsHome).ExecuteUpdateAsync(set => set.SetProperty(p => p.IsHome, false), cancellationToken);

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> change, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({PlacesLockKey})", cancellationToken);
            var result = await change();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: NameIndex })
        {
            // The check above runs under the lock; the index only backs it up.
            return typeof(T) == typeof(SavedPlaceOutcome)
                ? (T)(object)SavedPlaceOutcome.NameTaken
                : (T)(object)(SavedPlaceOutcome.NameTaken, (long?)null);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
