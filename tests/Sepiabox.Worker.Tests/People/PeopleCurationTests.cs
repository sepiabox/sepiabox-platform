using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Storage;
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
        // The named person stays, whichever order the two were given in (D-125).
        Assert.Equal((other.Id, 5, "Bruno"), (person.Id, person.FaceCount, person.Name));
        var faces = await db.Faces.AsNoTracking().ToListAsync();
        Assert.All(faces, f => Assert.Equal(other.Id, f.PersonId));
        Assert.Equal(2, faces.Count(f => f.Assignment == FaceAssignments.Manual));
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
        await Importer(db, container).ImportOneAsync(container.Name, BlobLayout.ManifestBlobName(first.Id), CancellationToken.None);

        var again = await db.Faces.AsNoTracking().SingleAsync(f => f.AssetId == first.Id);
        Assert.Equal((removed.Id, FaceAssignments.Excluded, (long?)person.Id), (again.Id, again.Assignment, again.PersonId));
        var after = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal(1, after.FaceCount);
        Assert.True(FaceMath.Cosine(SyntheticFaces.Near(face, seed: 2), after.Centroid!.ToArray()) > 0.99999);
    }

    [Fact]
    public async Task Keeps_a_face_marked_not_a_face_out_of_every_count_through_a_re_import_and_can_give_it_back()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var face = SyntheticFaces.Unit(seed: 1);
        var manifest = FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, face));
        await PutAsync(container, manifest);
        await PutAsync(container, FaceImportTests.WithFaces(2, FaceImportTests.Face(s_box, SyntheticFaces.Near(face, seed: 2))));
        await ImportAsync(db, container);
        var person = await db.People.AsNoTracking().SingleAsync();
        var marked = await db.Faces.AsNoTracking().SingleAsync(f => f.AssetId == manifest.Id);

        Assert.Equal(CurationOutcome.Done, await Curation(db).NotAFaceAsync(person.Id, marked.Id));
        await PutAsync(container, FaceImportTests.WithFaces(1, FaceImportTests.Face([0.11, 0.20, 0.30, 0.40], SyntheticFaces.Near(face, seed: 3))));
        await Importer(db, container).ImportOneAsync(container.Name, BlobLayout.ManifestBlobName(manifest.Id), CancellationToken.None);

        var again = await db.Faces.AsNoTracking().SingleAsync(f => f.Id == marked.Id);
        Assert.Equal((FaceAssignments.NotFace, (long?)person.Id), (again.Assignment, again.PersonId));
        var counted = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal(1, counted.FaceCount);
        Assert.True(FaceMath.Cosine(SyntheticFaces.Near(face, seed: 2), counted.Centroid!.ToArray()) > 0.99999);

        Assert.Equal(CurationOutcome.Done, await Curation(db).ReturnFaceAsync(person.Id, marked.Id));
        var back = await db.Faces.AsNoTracking().SingleAsync(f => f.Id == marked.Id);
        Assert.Equal((FaceAssignments.Manual, (long?)person.Id), (back.Assignment, back.PersonId));
        Assert.Equal(2, (await db.People.AsNoTracking().SingleAsync()).FaceCount);
    }

    [Fact]
    public async Task Lets_a_new_face_of_a_photo_join_a_person_whose_face_there_was_excluded()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var face = SyntheticFaces.Unit(seed: 1);
        var manifest = FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 7)));
        await PutAsync(container, manifest);
        await PutAsync(container, FaceImportTests.WithFaces(2, FaceImportTests.Face(s_box, face)));
        await ImportAsync(db, container);
        var person = await db.People.AsNoTracking().SingleAsync(p => p.Faces.Any(f => f.AssetId == FaceImportTests.WithFaces(2).Id));
        // The stranger of photo 1 is moved to this person by mistake, then excluded from them again.
        var stranger = await db.Faces.AsNoTracking().SingleAsync(f => f.AssetId == manifest.Id);
        await Curation(db).MoveFaceAsync(stranger.PersonId!.Value, stranger.Id, person.Id);
        await Curation(db).NotThisPersonAsync(person.Id, stranger.Id);

        // A newer run finds the person's real face in photo 1 too.
        await PutAsync(container, FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 7)), FaceImportTests.Face([0.6, 0.2, 0.3, 0.4], SyntheticFaces.Near(face, seed: 2))));
        await Importer(db, container).ImportOneAsync(container.Name, BlobLayout.ManifestBlobName(manifest.Id), CancellationToken.None);

        var joined = await db.Faces.AsNoTracking().SingleAsync(f => f.AssetId == manifest.Id && f.Box[0] == 0.6);
        Assert.Equal(person.Id, joined.PersonId);
        Assert.Equal(2, (await db.People.AsNoTracking().SingleAsync(p => p.Id == person.Id)).FaceCount);
    }

    [Fact]
    public async Task Keeps_an_unnamed_person_whose_only_face_is_marked_not_a_face()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 1))));
        await ImportAsync(db, container);
        var person = await db.People.AsNoTracking().SingleAsync();
        var face = await db.Faces.AsNoTracking().SingleAsync();

        Assert.Equal(CurationOutcome.Done, await Curation(db).NotAFaceAsync(person.Id, face.Id));

        // Still there, with nothing counted, so the mis-click can be undone from their page.
        var kept = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal((0, (Pgvector.Vector?)null), (kept.FaceCount, kept.Centroid));
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

    [Fact]
    public async Task Keeps_the_nearer_face_and_excludes_the_other_when_merged_people_share_a_photo()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var ana = SyntheticFaces.Unit(seed: 1);
        var stranger = SyntheticFaces.Unit(seed: 9);
        // Photo 1 has both faces; photos 2 and 3 have Ana alone.
        await PutAsync(container, FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, ana), FaceImportTests.Face([0.6, 0.2, 0.3, 0.4], stranger)));
        await PutAsync(container, FaceImportTests.WithFaces(2, FaceImportTests.Face(s_box, SyntheticFaces.Near(ana, seed: 2))));
        await PutAsync(container, FaceImportTests.WithFaces(3, FaceImportTests.Face(s_box, SyntheticFaces.Near(ana, seed: 3))));
        await ImportAsync(db, container);
        var anaFace = await db.Faces.AsNoTracking().SingleAsync(f => f.AssetId == FaceImportTests.WithFaces(1).Id && f.Box[0] == 0.1);
        var strangerFace = await db.Faces.AsNoTracking().SingleAsync(f => f.AssetId == FaceImportTests.WithFaces(1).Id && f.Box[0] == 0.6);

        Assert.Equal(CurationOutcome.Done, await Curation(db).MergeAsync(anaFace.PersonId!.Value, strangerFace.PersonId!.Value));

        var person = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal((anaFace.PersonId, 3), (person.Id, person.FaceCount));
        var excluded = await db.Faces.AsNoTracking().SingleAsync(f => f.Id == strangerFace.Id);
        Assert.Equal((FaceAssignments.Excluded, (long?)person.Id), (excluded.Assignment, excluded.PersonId));
    }

    [Fact]
    public async Task Gives_back_an_excluded_face_as_the_family_s_choice()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var face = SyntheticFaces.Unit(seed: 1);
        await PutAsync(container, FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, face)));
        await PutAsync(container, FaceImportTests.WithFaces(2, FaceImportTests.Face(s_box, SyntheticFaces.Near(face, seed: 2))));
        await ImportAsync(db, container);
        var person = await db.People.AsNoTracking().SingleAsync();
        var removed = await db.Faces.AsNoTracking().OrderBy(f => f.Id).FirstAsync();
        await Curation(db).NotThisPersonAsync(person.Id, removed.Id);

        Assert.Equal(CurationOutcome.Done, await Curation(db).ReturnFaceAsync(person.Id, removed.Id));

        var back = await db.Faces.AsNoTracking().SingleAsync(f => f.Id == removed.Id);
        Assert.Equal((FaceAssignments.Manual, (long?)person.Id), (back.Assignment, back.PersonId));
        Assert.Equal(2, (await db.People.AsNoTracking().SingleAsync()).FaceCount);
        // A face that is not excluded has nothing to give back.
        Assert.Equal(CurationOutcome.NotFound, await Curation(db).ReturnFaceAsync(person.Id, removed.Id));
    }

    [Fact]
    public async Task Moves_a_face_to_another_person_or_a_new_one_and_removes_a_person_left_with_nothing()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 1))));
        await PutAsync(container, FaceImportTests.WithFaces(2, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 3))));
        await ImportAsync(db, container);
        var (first, second) = await TwoPeopleAsync(db);
        var face = await db.Faces.AsNoTracking().SingleAsync(f => f.PersonId == first.Id);

        Assert.Equal(CurationOutcome.Done, await Curation(db).MoveFaceAsync(first.Id, face.Id, second.Id));

        var moved = await db.Faces.AsNoTracking().SingleAsync(f => f.Id == face.Id);
        Assert.Equal((FaceAssignments.Manual, (long?)second.Id), (moved.Assignment, moved.PersonId));
        var people = await db.People.AsNoTracking().ToListAsync();
        Assert.Equal((second.Id, 2), (Assert.Single(people).Id, people[0].FaceCount));

        Assert.Equal(CurationOutcome.Done, await Curation(db).MoveFaceAsync(second.Id, face.Id, targetId: null));
        var made = await db.People.AsNoTracking().SingleAsync(p => p.Id != second.Id);
        Assert.Equal((null, 1), (made.Name, made.FaceCount));
        Assert.Equal(1, (await db.People.AsNoTracking().SingleAsync(p => p.Id == second.Id)).FaceCount);
    }

    [Fact]
    public async Task Refuses_to_give_a_person_two_faces_of_one_photo()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 1)), FaceImportTests.Face([0.6, 0.2, 0.3, 0.4], SyntheticFaces.Unit(seed: 3))));
        await ImportAsync(db, container);
        var (first, second) = await TwoPeopleAsync(db);
        var face = await db.Faces.AsNoTracking().SingleAsync(f => f.PersonId == first.Id);
        var theirs = await db.Faces.AsNoTracking().SingleAsync(f => f.PersonId == second.Id);

        Assert.Equal(CurationOutcome.PhotoTaken, await Curation(db).MoveFaceAsync(first.Id, face.Id, second.Id));
        await Curation(db).NotThisPersonAsync(second.Id, theirs.Id);
        Assert.Equal(CurationOutcome.Done, await Curation(db).MoveFaceAsync(first.Id, face.Id, second.Id));
        Assert.Equal(CurationOutcome.PhotoTaken, await Curation(db).ReturnFaceAsync(second.Id, theirs.Id));
    }

    [Fact]
    public async Task Changes_nothing_when_the_face_belongs_to_another_person()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 1))));
        await PutAsync(container, FaceImportTests.WithFaces(2, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 3))));
        await ImportAsync(db, container);
        var (first, second) = await TwoPeopleAsync(db);
        var theirs = await db.Faces.AsNoTracking().SingleAsync(f => f.PersonId == second.Id);
        await Curation(db).NotThisPersonAsync(second.Id, theirs.Id);
        var before = await db.Faces.AsNoTracking().SingleAsync(f => f.Id == theirs.Id);

        Assert.Equal(CurationOutcome.NotFound, await Curation(db).NotThisPersonAsync(first.Id, theirs.Id));
        Assert.Equal(CurationOutcome.NotFound, await Curation(db).NotAFaceAsync(first.Id, theirs.Id));
        Assert.Equal(CurationOutcome.NotFound, await Curation(db).ReturnFaceAsync(first.Id, theirs.Id));
        Assert.Equal(CurationOutcome.NotFound, await Curation(db).MoveFaceAsync(first.Id, theirs.Id, targetId: null));

        var after = await db.Faces.AsNoTracking().SingleAsync(f => f.Id == theirs.Id);
        Assert.Equal((before.Assignment, before.PersonId), (after.Assignment, after.PersonId));
        Assert.Equal(2, await db.People.CountAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Keeps_the_named_person_the_user_s_person_or_the_larger_one_whatever_order_they_are_given_in(bool swapOrder, bool linkedStays)
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        // The named one has 3 faces, the unnamed 12; with a link, the linked unnamed 2-face person beats the named one of 30.
        var (stays, leaves) = linkedStays
            ? (await PersonAsync(db, null, 2, 1, user: Ana), await PersonAsync(db, "Carla", 30, 100))
            : (await PersonAsync(db, "Dora", 3, 1), await PersonAsync(db, null, 12, 100));
        var (first, second) = swapOrder ? (leaves, stays) : (stays, leaves);

        Assert.Equal(CurationOutcome.Done, await Curation(db).MergeAsync(first, second));

        var person = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal(stays, person.Id);
        Assert.Equal(linkedStays ? 32 : 15, await db.Faces.CountAsync(f => f.PersonId == stays));
    }

    [Fact]
    public async Task Keeps_the_person_with_more_faces_when_both_are_named_or_neither_is_and_the_older_on_a_tie()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var small = await PersonAsync(db, "Eva", 2, 1);
        var large = await PersonAsync(db, "Fábio", 5, 100);
        Assert.Equal(CurationOutcome.Done, await Curation(db).MergeAsync(small, large));
        Assert.Equal(large, (await db.People.AsNoTracking().SingleAsync()).Id);

        await using var tied = await fixture.CreateMigratedDatabaseAsync();
        var younger = await PersonAsync(tied, null, 3, 1);
        var older = await PersonAsync(tied, null, 3, 100);
        Assert.Equal(CurationOutcome.Done, await Curation(tied).MergeAsync(older, younger));
        Assert.Equal(Math.Min(younger, older), (await tied.People.AsNoTracking().SingleAsync()).Id);
    }

    [Fact]
    public async Task Merges_the_other_way_only_when_the_swap_is_explicit_and_the_name_goes_with_the_one_that_stays()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var named = await PersonAsync(db, "Gil", 3, 1);
        var unnamed = await PersonAsync(db, null, 12, 100);

        Assert.Equal(CurationOutcome.Done, await Curation(db).MergeAsync(named, unnamed, swap: true));

        var person = await db.People.AsNoTracking().SingleAsync();
        // The unnamed person stays, and takes the name of the one that left, as a merge always did.
        Assert.Equal((unnamed, "Gil", 15), (person.Id, person.Name, person.FaceCount));
    }

    [Fact]
    public async Task A_stale_or_hand_made_request_cannot_delete_a_named_or_linked_person_without_a_swap()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var named = await PersonAsync(db, "Hana", 1500, 1);
        var unnamed = await PersonAsync(db, null, 100, 5000);
        var me = await PersonAsync(db, null, 1, 9000, user: Ana);

        // The page said the unnamed one stays; the request still cannot make the named one the one that goes.
        Assert.Equal(CurationOutcome.Done, await Curation(db).MergeAsync(unnamed, named));
        Assert.Equal([named, me], (await db.People.AsNoTracking().OrderBy(p => p.Id).Select(p => p.Id).ToListAsync()));
        Assert.Equal(CurationOutcome.Done, await Curation(db).MergeAsync(named, me));
        var person = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal((me, Ana, "Hana"), (person.Id, person.UserEmail, person.Name));
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
        Importer(db, container).ImportOnceAsync(container.Name, CancellationToken.None);

    private static ManifestImporter Importer(CatalogDbContext db, BlobContainerClient container) =>
        new(db, TestBlobStore.Of(container), new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance);

    private static async Task PutAsync(BlobContainerClient container, Manifest manifest) =>
        await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id))
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(manifest)), overwrite: true);

    // A person with n counted faces, each in a photo of its own (ids from the base), made by hand: the importer is not the subject here.
    private static async Task<long> PersonAsync(CatalogDbContext db, string? name, int faces, int baseId, string? user = null)
    {
        var person = new Person { Name = name, UserEmail = user, FaceCount = faces };
        db.People.Add(person);
        for (var i = 0; i < faces; i++)
        {
            var id = (baseId + i).ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
            db.Assets.Add(new Asset { Id = id, Kind = "photo", ContentType = "image/jpeg", SizeBytes = 1 });
            db.Faces.Add(new Face { AssetId = id, Person = person, Score = 0.9, Box = [0.1, 0.1, 0.2, 0.2], Embedding = new Pgvector.Vector(SyntheticFaces.Unit(seed: baseId + i)), Assignment = FaceAssignments.Auto });
        }

        await db.SaveChangesAsync();
        return person.Id;
    }
}
