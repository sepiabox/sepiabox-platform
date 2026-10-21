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

    /// <summary>The person already has a face in that photo: one face of a photo per person.</summary>
    PhotoTaken,

    /// <summary>The name has control characters.</summary>
    NameInvalid,
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
        if (CleanName(name, out var clean) is { } refused)
        {
            return Task.FromResult(refused);
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

            // One face of a photo per person: where both had a face in the same photo, the one nearer the merged
            // centroid stays and the other is excluded, still pointing here, to be moved where it belongs.
            var ours = await db.Faces.Where(f => f.PersonId == personId).ToListAsync(cancellationToken);
            var counted = ours.Concat(faces).Where(f => FaceAssignments.Counts(f.Assignment)).ToList();
            if (FaceMath.Centroid(counted.Select(f => f.Embedding.ToArray())) is { } centroid)
            {
                foreach (var photo in counted.GroupBy(f => f.AssetId).Where(g => g.Count() > 1))
                {
                    var keep = photo.OrderByDescending(f => FaceMath.Cosine(centroid, f.Embedding.ToArray())).ThenBy(f => f.Id).First();
                    foreach (var face in photo.Where(f => f != keep))
                    {
                        face.Assignment = FaceAssignments.Excluded;
                    }
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
        MarkAsync(personId, faceId, FaceAssignments.Excluded, cancellationToken);

    /// <summary>
    /// "Not a face": the detection was wrong. Like "Not this person" it counts for nobody and stays through re-imports,
    /// marked <c>not_face</c> and still pointing to the person, so a mis-click can be given back.
    /// </summary>
    public Task<CurationOutcome> NotAFaceAsync(long personId, long faceId, CancellationToken cancellationToken = default) =>
        MarkAsync(personId, faceId, FaceAssignments.NotFace, cancellationToken);

    /// <summary>
    /// "Give back": a face excluded from this person, or marked not a face (still pointing to them), is theirs again, as
    /// the family's choice (<c>manual</c>); refused when the person already has a face in that photo.
    /// </summary>
    public Task<CurationOutcome> ReturnFaceAsync(long personId, long faceId, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            var face = await db.Faces.SingleOrDefaultAsync(
                f => f.Id == faceId && f.PersonId == personId
                    && (f.Assignment == FaceAssignments.Excluded || f.Assignment == FaceAssignments.NotFace),
                cancellationToken);
            if (face is null || await db.People.FindAsync([personId], cancellationToken) is not { } person)
            {
                return CurationOutcome.NotFound;
            }

            if (await HasFaceInPhotoAsync(personId, face, cancellationToken))
            {
                return CurationOutcome.PhotoTaken;
            }

            face.Assignment = FaceAssignments.Manual;
            await db.SaveChangesAsync(cancellationToken);
            await _assigner.RecomputeAsync(person, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return CurationOutcome.Done;
        }, cancellationToken);

    /// <summary>
    /// "Move to…": a face of this person (shown or excluded) goes to <paramref name="targetId"/>, or to a new unnamed
    /// person when null, as the family's choice (<c>manual</c>). Refused when the target already has a face in that
    /// photo. This person is recomputed, and goes when unnamed with no face row left (D-077).
    /// </summary>
    public Task<CurationOutcome> MoveFaceAsync(long personId, long faceId, long? targetId, CancellationToken cancellationToken = default)
    {
        if (targetId == personId)
        {
            return Task.FromResult(CurationOutcome.SamePerson);
        }

        return InTransactionAsync(async () =>
        {
            var face = await db.Faces.SingleOrDefaultAsync(f => f.Id == faceId && f.PersonId == personId, cancellationToken);
            if (face is null || await db.People.FindAsync([personId], cancellationToken) is not { } person)
            {
                return CurationOutcome.NotFound;
            }

            Person target;
            if (targetId is { } id)
            {
                if (await db.People.FindAsync([id], cancellationToken) is not { } existing)
                {
                    return CurationOutcome.NotFound;
                }

                if (await HasFaceInPhotoAsync(id, face, cancellationToken))
                {
                    return CurationOutcome.PhotoTaken;
                }

                target = existing;
            }
            else
            {
                target = new Person();
                db.People.Add(target);
                await db.SaveChangesAsync(cancellationToken);
            }

            face.PersonId = target.Id;
            face.Assignment = FaceAssignments.Manual;
            await db.SaveChangesAsync(cancellationToken);
            await _assigner.RecomputeAsync(target, cancellationToken);
            await _assigner.RecomputeAsync(person, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return CurationOutcome.Done;
        }, cancellationToken);
    }

    // Whether the person already has a counted face, other than this one, in this face's photo.
    private Task<bool> HasFaceInPhotoAsync(long personId, Face face, CancellationToken cancellationToken) =>
        db.Faces.AnyAsync(
            f => f.PersonId == personId && f.AssetId == face.AssetId && f.Id != face.Id
                && (f.Assignment == FaceAssignments.Auto || f.Assignment == FaceAssignments.Manual),
            cancellationToken);

    private Task<CurationOutcome> MarkAsync(long personId, long faceId, string assignment, CancellationToken cancellationToken) =>
        InTransactionAsync(async () =>
        {
            var face = await db.Faces.SingleOrDefaultAsync(f => f.Id == faceId && f.PersonId == personId, cancellationToken);
            if (face is null || await db.People.FindAsync([personId], cancellationToken) is not { } person)
            {
                return CurationOutcome.NotFound;
            }

            face.Assignment = assignment;
            await db.SaveChangesAsync(cancellationToken);
            await _assigner.RecomputeAsync(person, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return CurationOutcome.Done;
        }, cancellationToken);

    /// <summary>
    /// A name as stored (D-079): NFC, without invisible formatting characters (Unicode category Cf), every run of white
    /// space one space, trimmed; null for a blank one. Returns why it is refused, or null: control characters, or more than
    /// <see cref="MaxNameLength"/> characters.
    /// </summary>
    public static CurationOutcome? CleanName(string? name, out string? clean)
    {
        clean = null;
        if (name is null)
        {
            return null;
        }

        var text = new System.Text.StringBuilder(name.Length);
        var space = false;
        foreach (var c in name)
        {
            // Invisible formatting (zero-width spaces and joiners, the soft hyphen, bidirectional overrides such as
            // U+202E): never part of a name, and able to make two names look alike or a name read backwards.
            if (char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }

            if (char.IsControl(c))
            {
                return CurationOutcome.NameInvalid;
            }

            if (space && text.Length > 0)
            {
                text.Append(' ');
            }

            space = false;
            text.Append(c);
        }

        var normalized = text.ToString().Normalize(System.Text.NormalizationForm.FormC);
        if (normalized.Length > MaxNameLength)
        {
            return CurationOutcome.NameTooLong;
        }

        clean = normalized.Length == 0 ? null : normalized;
        return null;
    }

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
