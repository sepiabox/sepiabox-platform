using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Worker.Importing;

namespace Sepiabox.Worker.Tests.People;

/// <summary>
/// The family's corrections to people (S-020) on a real PostgreSQL, and how imports respect them. Synthetic vectors
/// and example.com addresses only.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class PeopleCurationTests(CatalogFixture fixture)
{
    private const string Ana = "ana@example.com";
    private const string Bruno = "bruno@example.com";
    private static readonly double[] s_box = [0.10, 0.20, 0.30, 0.40];

    [Fact]
    public async Task Moves_the_faces_and_adds_up_the_counts_when_two_people_are_merged()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        float[][] a = [SyntheticFaces.Unit(seed: 1), SyntheticFaces.Near(SyntheticFaces.Unit(seed: 1), seed: 2)];
        float[][] b = [SyntheticFaces.Unit(seed: 3), SyntheticFaces.Near(SyntheticFaces.Unit(seed: 3), seed: 4), SyntheticFaces.Near(SyntheticFaces.Unit(seed: 3), seed: 5)];
        for (var n = 0; n < a.Length + b.Length; n++)
        {
            await PutAsync(container, FaceImportTests.WithFaces(n + 1, FaceImportTests.Face(s_box, n < a.Length ? a[n] : b[n - a.Length])));
        }

        await ImportAsync(db, container);
        var (keep, other) = await TwoPeopleAsync(db);
        Assert.Equal(CurationOutcome.Done, await Curation(db).RenameAsync(other.Id, "Bruno"));

        var outcome = await Curation(db).MergeAsync(keep.Id, other.Id);

        Assert.Equal(CurationOutcome.Done, outcome);
        var person = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal((keep.Id, 5, "Bruno"), (person.Id, person.FaceCount, person.Name));
        var faces = await db.Faces.AsNoTracking().ToListAsync();
        Assert.All(faces, f => Assert.Equal(keep.Id, f.PersonId));
        Assert.Equal(3, faces.Count(f => f.Assignment == FaceAssignments.Manual));
        var centroid = person.Centroid!.ToArray();
        var expected = FaceMath.Centroid([.. a, .. b])!;
        Assert.True(FaceMath.Cosine(expected, centroid) > 0.99999);
    }

    [Fact]
    public async Task Never_gives_a_face_back_to_a_person_it_was_removed_from_on_a_re_import()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var face = SyntheticFaces.Unit(seed: 1);
        var first = FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, face));
        await PutAsync(container, first);
        await PutAsync(container, FaceImportTests.WithFaces(2, FaceImportTests.Face(s_box, SyntheticFaces.Near(face, seed: 2))));
        await ImportAsync(db, container);
        var person = await db.People.AsNoTracking().SingleAsync();
        var removed = await db.Faces.AsNoTracking().SingleAsync(f => f.AssetId == first.Id);

        Assert.Equal(CurationOutcome.Done, await Curation(db).NotThisPersonAsync(person.Id, removed.Id));
        Assert.Equal(1, (await db.People.AsNoTracking().SingleAsync()).FaceCount);
        // Imported again, as after another computer's upload: same box, slightly different numbers.
        await PutAsync(container, FaceImportTests.WithFaces(1, FaceImportTests.Face([0.11, 0.20, 0.30, 0.40], SyntheticFaces.Near(face, seed: 3))));
        await Importer(db).ImportOneAsync(container, BlobLayout.ManifestBlobName(first.Id), CancellationToken.None);

        var again = await db.Faces.AsNoTracking().SingleAsync(f => f.AssetId == first.Id);
        Assert.Equal((removed.Id, FaceAssignments.Excluded, (long?)person.Id), (again.Id, again.Assignment, again.PersonId));
        var after = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal(1, after.FaceCount);
        Assert.True(FaceMath.Cosine(SyntheticFaces.Near(face, seed: 2), after.Centroid!.ToArray()) > 0.99999);
    }

    [Fact]
    public async Task Leaves_a_face_marked_not_a_face_with_nobody_after_a_re_import()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var manifest = FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 1)));
        await PutAsync(container, manifest);
        await ImportAsync(db, container);
        var person = await db.People.AsNoTracking().SingleAsync();
        var face = await db.Faces.AsNoTracking().SingleAsync();

        Assert.Equal(CurationOutcome.Done, await Curation(db).NotAFaceAsync(person.Id, face.Id));
        await Importer(db).ImportOneAsync(container, BlobLayout.ManifestBlobName(manifest.Id), CancellationToken.None);

        var again = await db.Faces.AsNoTracking().SingleAsync();
        Assert.Equal((FaceAssignments.Excluded, (long?)null), (again.Assignment, again.PersonId));
        // The unnamed person had nothing else: gone.
        Assert.Equal(0, await db.People.CountAsync());
    }

    [Fact]
    public async Task Rejects_this_is_me_from_a_second_user_on_the_same_person()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var (person, other) = await TwoPeopleAsync(db, imported: false);

        Assert.Equal(CurationOutcome.Done, await Curation(db).LinkUserAsync(person.Id, "Ana@Example.com"));
        Assert.Equal(CurationOutcome.Done, await Curation(db).LinkUserAsync(person.Id, Ana));
        Assert.Equal(CurationOutcome.ClaimedByAnother, await Curation(db).LinkUserAsync(person.Id, Bruno));
        // One person per user.
        Assert.Equal(CurationOutcome.AlreadyLinked, await Curation(db).LinkUserAsync(other.Id, Ana));
        Assert.Equal(CurationOutcome.ClaimedByAnother, await Curation(db).UnlinkUserAsync(person.Id, Bruno));
        Assert.Equal(Ana, (await db.People.AsNoTracking().SingleAsync(p => p.Id == person.Id)).UserEmail);
        Assert.Equal(CurationOutcome.Done, await Curation(db).UnlinkUserAsync(person.Id, Ana));
        Assert.Equal(CurationOutcome.Done, await Curation(db).LinkUserAsync(other.Id, Ana));
    }

    [Fact]
    public async Task Rejects_a_name_another_person_has_ignoring_case()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var (person, other) = await TwoPeopleAsync(db, imported: false);

        Assert.Equal(CurationOutcome.Done, await Curation(db).RenameAsync(person.Id, "  Ana  "));
        Assert.Equal(CurationOutcome.NameTaken, await Curation(db).RenameAsync(other.Id, "ANA"));
        Assert.Equal(CurationOutcome.Done, await Curation(db).RenameAsync(person.Id, "ana"));
        Assert.Equal(CurationOutcome.NameTooLong, await Curation(db).RenameAsync(other.Id, new string('a', PeopleCuration.MaxNameLength + 1)));

        var names = await db.People.AsNoTracking().OrderBy(p => p.Id).Select(p => p.Name).ToListAsync();
        Assert.Equal(["ana", null], names);
    }

    [Fact]
    public async Task Refuses_to_merge_two_people_linked_to_two_users()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var (person, other) = await TwoPeopleAsync(db, imported: false);
        await Curation(db).LinkUserAsync(person.Id, Ana);
        await Curation(db).LinkUserAsync(other.Id, Bruno);

        Assert.Equal(CurationOutcome.DifferentUsers, await Curation(db).MergeAsync(person.Id, other.Id));
        Assert.Equal(CurationOutcome.SamePerson, await Curation(db).MergeAsync(person.Id, person.Id));
        Assert.Equal(2, await db.People.CountAsync());
    }

    [Fact]
    public async Task Waits_for_an_import_that_holds_the_people_lock()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var (person, _) = await TwoPeopleAsync(db, imported: false);
        await using var importing = Context(fixture.ConnectionStringFor(db));
        await using var transaction = await importing.Database.BeginTransactionAsync();
        await FaceAssigner.LockAsync(importing, CancellationToken.None);

        await using var editing = Context(fixture.ConnectionStringFor(db));
        var rename = new PeopleCuration(editing).RenameAsync(person.Id, "Carla");
        await Task.Delay(500);
        Assert.False(rename.IsCompleted);

        await transaction.CommitAsync();
        Assert.Equal(CurationOutcome.Done, await rename);
    }

    private static PeopleCuration Curation(CatalogDbContext db) => new(db);

    // Two people, either from imported faces or made directly, oldest first.
    private static async Task<(Person First, Person Second)> TwoPeopleAsync(CatalogDbContext db, bool imported = true)
    {
        if (!imported)
        {
            db.People.AddRange(new Person(), new Person());
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        var people = await db.People.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        Assert.Equal(2, people.Count);
        return (people[0], people[1]);
    }

    private static CatalogDbContext Context(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContext.Configure(options, connectionString);
        return new CatalogDbContext(options.Options);
    }

    private static Task<ImportResult> ImportAsync(CatalogDbContext db, BlobContainerClient container) =>
        Importer(db).ImportOnceAsync(container, CancellationToken.None);

    private static ManifestImporter Importer(CatalogDbContext db) =>
        new(db, new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance);

    private static async Task PutAsync(BlobContainerClient container, Manifest manifest) =>
        await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id))
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(manifest)), overwrite: true);
}
