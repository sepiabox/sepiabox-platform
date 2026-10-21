using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Sepiabox.Catalog.People;

/// <summary>What became of a change the family asked for (S-020).</summary>
public enum CurationOutcome
{
    Done,

    /// <summary>The person (or the face, for that person) is not in the catalog any more.</summary>
    NotFound,

    /// <summary>Another person already has that name, ignoring case.</summary>
    NameTaken,

    /// <summary>The name is longer than <see cref="PeopleCuration.MaxNameLength"/>.</summary>
    NameTooLong,

    /// <summary>Someone else said "This is me" for this person.</summary>
    ClaimedByAnother,

    /// <summary>This user already said "This is me" for another person: one person per user.</summary>
    AlreadyLinked,

    /// <summary>A person cannot be merged with themselves.</summary>
    SamePerson,

    /// <summary>The two people are linked to two different users, who cannot be one person.</summary>
    DifferentUsers,
}

/// <summary>
/// The family's corrections to people (S-020, D-079): names, "This is me", merging two people, "Not this person" and
/// "Not a face". Each change is one transaction that first takes the same lock as the importer's face assignment
/// (<see cref="FaceAssigner.LockAsync"/>), saves through SaveChanges, then recomputes the people it touched as the
/// importer does, so an import and an edit never count over each other. A face the family moved or excluded keeps that
/// choice on every re-import (D-077's carry-over), and the importer never assigns faces to it again.
/// </summary>
public sealed class PeopleCuration(CatalogDbContext db)
{
    public const int MaxNameLength = 200;

    /// <summary>The unique index on <c>lower(name)</c>, added by the migration <c>PeopleUnique</c>.</summary>
    public const string NameIndex = "ix_people_lower_name";

    /// <summary>The unique index on <c>user_email</c> (stored lower-cased): one person per user.</summary>
    public const string UserIndex = "ix_people_user_email";

    private readonly FaceAssigner _assigner = new(db);

    /// <summary>Names the person, or clears the name for a blank one. Names are unique, ignoring case.</summary>
    public Task<CurationOutcome> RenameAsync(long personId, string? name, CancellationToken cancellationToken = default)
    {
        var clean = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (clean?.Length > MaxNameLength)
        {
            return Task.FromResult(CurationOutcome.NameTooLong);
        }

        return InTransactionAsync(async () =>
        {
            if (await db.People.FindAsync([personId], cancellationToken) is not { } person)
            {
                return CurationOutcome.NotFound;
            }

            if (clean is not null)
            {
                // lower() in SQL, as the unique index compares them.
                string[] lowered = [clean.ToLowerInvariant()];
                if (await db.People.AnyAsync(p => p.Id != personId && p.Name != null && lowered.Contains(p.Name.ToLower()), cancellationToken))
                {
                    return CurationOutcome.NameTaken;
                }
            }

            person.Name = clean;
            await db.SaveChangesAsync(cancellationToken);
            // A person left with no face and now no name goes, as the importer would remove them.
            await _assigner.RecomputeAsync(person, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return CurationOutcome.Done;
        }, cancellationToken);
    }

    /// <summary>
    /// "This is me": links <paramref name="email"/> to the person (D-018). Rejected when another user already did, or
    /// when this user is linked to another person; asking again for the same person changes nothing.
    /// </summary>
    public Task<CurationOutcome> LinkUserAsync(long personId, string email, CancellationToken cancellationToken = default)
    {
        var user = NormalizeEmail(email);
        return InTransactionAsync(async () =>
        {
            if (await db.People.FindAsync([personId], cancellationToken) is not { } person)
            {
                return CurationOutcome.NotFound;
            }

            if (person.UserEmail is { } linked)
            {
                return linked == user ? CurationOutcome.Done : CurationOutcome.ClaimedByAnother;
            }

            if (await db.People.AnyAsync(p => p.Id != personId && p.UserEmail == user, cancellationToken))
            {
                return CurationOutcome.AlreadyLinked;
            }

            person.UserEmail = user;
            await db.SaveChangesAsync(cancellationToken);
            return CurationOutcome.Done;
        }, cancellationToken);
    }

    /// <summary>"This is not me": only the user who is linked can undo their own link.</summary>
    public Task<CurationOutcome> UnlinkUserAsync(long personId, string email, CancellationToken cancellationToken = default)
    {
        var user = NormalizeEmail(email);
        return InTransactionAsync(async () =>
        {
            if (await db.People.FindAsync([personId], cancellationToken) is not { } person)
            {
                return CurationOutcome.NotFound;
            }

            if (person.UserEmail is { } linked && linked != user)
            {
                return CurationOutcome.ClaimedByAnother;
            }

            person.UserEmail = null;
            await db.SaveChangesAsync(cancellationToken);
            await _assigner.RecomputeAsync(person, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return CurationOutcome.Done;
        }, cancellationToken);
    }

    /// <summary>
    /// Merges <paramref name="otherId"/> into <paramref name="personId"/>: every face of the other person (excluded ones
    /// included, still excluded) moves to this one, the family's choice from now on (<c>manual</c>), the other person
    /// goes, and this one keeps its name, or takes the other's when it has none, and its user link, or the other's.
    /// </summary>
    public Task<CurationOutcome> MergeAsync(long personId, long otherId, CancellationToken cancellationToken = default)
    {
        if (personId == otherId)
        {
            return Task.FromResult(CurationOutcome.SamePerson);
        }

        return InTransactionAsync(async () =>
        {
            if (await db.People.FindAsync([personId], cancellationToken) is not { } person
                || await db.People.FindAsync([otherId], cancellationToken) is not { } other)
            {
                return CurationOutcome.NotFound;
            }

            if (person.UserEmail is not null && other.UserEmail is not null && person.UserEmail != other.UserEmail)
            {
                return CurationOutcome.DifferentUsers;
            }

            var faces = await db.Faces.Where(f => f.PersonId == otherId).ToListAsync(cancellationToken);
            foreach (var face in faces)
            {
                face.PersonId = personId;
                if (face.Assignment == FaceAssignments.Auto)
                {
                    face.Assignment = FaceAssignments.Manual;
                }
            }

            var name = person.Name ?? other.Name;
            var user = person.UserEmail ?? other.UserEmail;
            db.People.Remove(other);
            // The other person's name and link are free before this one takes them: both are unique.
            await db.SaveChangesAsync(cancellationToken);
            person.Name = name;
            person.UserEmail = user;
            await db.SaveChangesAsync(cancellationToken);
            await _assigner.RecomputeAsync(person, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return CurationOutcome.Done;
        }, cancellationToken);
    }

    /// <summary>
    /// "Not this person": the face stays with the person it was excluded from, as <c>excluded</c>, so it counts for
    /// nobody, is in no centroid, and no re-import gives it back to them (its row and choice are kept, D-077).
    /// </summary>
    public Task<CurationOutcome> NotThisPersonAsync(long personId, long faceId, CancellationToken cancellationToken = default) =>
        ExcludeAsync(personId, faceId, keepPerson: true, cancellationToken);

    /// <summary>"Not a face": the detection was wrong; the face belongs to nobody and is excluded for good.</summary>
    public Task<CurationOutcome> NotAFaceAsync(long personId, long faceId, CancellationToken cancellationToken = default) =>
        ExcludeAsync(personId, faceId, keepPerson: false, cancellationToken);

    private Task<CurationOutcome> ExcludeAsync(long personId, long faceId, bool keepPerson, CancellationToken cancellationToken) =>
        InTransactionAsync(async () =>
        {
            var face = await db.Faces.SingleOrDefaultAsync(f => f.Id == faceId && f.PersonId == personId, cancellationToken);
            if (face is null || await db.People.FindAsync([personId], cancellationToken) is not { } person)
            {
                return CurationOutcome.NotFound;
            }

            face.Assignment = FaceAssignments.Excluded;
            face.PersonId = keepPerson ? personId : null;
            await db.SaveChangesAsync(cancellationToken);
            await _assigner.RecomputeAsync(person, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return CurationOutcome.Done;
        }, cancellationToken);

    // Lower-cased and trimmed, as the access list compares addresses.
    private static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return email.Trim().ToLowerInvariant();
    }

    private async Task<CurationOutcome> InTransactionAsync(Func<Task<CurationOutcome>> change, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await FaceAssigner.LockAsync(db, cancellationToken);
            var outcome = await change();
            if (outcome == CurationOutcome.Done)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return outcome;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique)
        {
            // The checks above run under the lock; the unique indexes only back them up.
            return unique.ConstraintName == UserIndex ? CurationOutcome.ClaimedByAnother : CurationOutcome.NameTaken;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
