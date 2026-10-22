using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Storage;
using Sepiabox.Worker.Faces;
using Sepiabox.Worker.Importing;

namespace Sepiabox.Worker.Tests.People;

/// <summary>
/// Turning face recognition off, deleting a person's face data and sweeping the erase into the stored manifests (S-022),
/// on a real PostgreSQL and Azurite. Synthetic vectors and example.com addresses only.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class FaceRecognitionTests(CatalogFixture fixture)
{
    private const string Ana = "ana@example.com";
    private static readonly double[] s_box = [0.10, 0.20, 0.30, 0.40];

    [Fact]
    public async Task Turning_recognition_off_empties_faces_and_people_and_records_who_did_it()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 1))));
        await PutAsync(container, FaceImportTests.WithFaces(2, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 3))));
        await ImportAsync(db, container);
        var person = await db.People.AsNoTracking().OrderBy(p => p.Id).FirstAsync();
        Assert.Equal(CurationOutcome.Done, await new PeopleCuration(db).RenameAsync(person.Id, "Ana"));
        Assert.Equal(CurationOutcome.Done, await new PeopleCuration(db).LinkUserAsync(person.Id, Ana));

        var deletion = await Recognition(db).TurnOffAsync(" Ana@Example.com ");

        Assert.Equal(0, await db.Faces.CountAsync());
        Assert.Equal(0, await db.People.CountAsync());
        Assert.Equal(2, await db.Assets.CountAsync());
        Assert.All(await db.Assets.AsNoTracking().ToListAsync(), a => Assert.Null(a.FacesVersion));
        var recorded = await db.FaceDataDeletions.AsNoTracking().SingleAsync();
        Assert.Equal((FaceDataDeletionScopes.Library, 2, 2, Ana), (recorded.Scope, recorded.Faces, recorded.People, recorded.DeletedBy));
        Assert.Equal(deletion!.DeletedAt, recorded.DeletedAt);
        var state = await FaceRecognition.ReadAsync(db);
        Assert.False(state.On);
        Assert.Equal(deletion.DeletedAt, state.ErasedAt);
        Assert.True(state.ManifestsPending);
    }

    [Fact]
    public async Task Stores_no_face_from_a_manifest_imported_while_recognition_is_off()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await Recognition(db).TurnOffAsync(Ana);
        var erasedAt = (await FaceRecognition.ReadAsync(db)).ErasedAt!.Value;
        // Even faces found after the erase: recognition is off.
        await PutAsync(container, FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 1))) with
        {
            FacesDetectedAt = erasedAt.AddDays(1),
        });

        var result = await ImportAsync(db, container);

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, await db.Assets.CountAsync());
        Assert.Equal(0, await db.Faces.CountAsync());
        Assert.Equal(0, await db.People.CountAsync());
        Assert.Null((await db.Assets.AsNoTracking().SingleAsync()).FacesVersion);
    }

    [Fact]
    public async Task Imports_only_faces_found_after_the_erase_once_recognition_is_back_on()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await Recognition(db).TurnOffAsync(Ana);
        var erasedAt = (await FaceRecognition.ReadAsync(db)).ErasedAt!.Value;
        Assert.True(await Recognition(db).MarkSweptAsync(erasedAt, erasedAt));
        Assert.True(await Recognition(db).TurnOnAsync());
        var before = FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 1))) with { FacesDetectedAt = erasedAt.AddDays(-1) };
        var unknown = FaceImportTests.WithFaces(2, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 3)));
        var atTheErase = FaceImportTests.WithFaces(3, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 4))) with { FacesDetectedAt = erasedAt };
        var after = FaceImportTests.WithFaces(4, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 5))) with { FacesDetectedAt = erasedAt.AddSeconds(1) };
        foreach (var manifest in new[] { before, unknown, atTheErase, after })
        {
            await PutAsync(container, manifest);
        }

        await ImportAsync(db, container);

        Assert.Equal(4, await db.Assets.CountAsync());
        Assert.Equal([after.Id], await db.Faces.Select(f => f.AssetId).ToListAsync());
        Assert.True((await FaceRecognition.ReadAsync(db)).On);
    }

    [Fact]
    public async Task Refuses_to_turn_recognition_back_on_until_the_manifests_carry_the_erase()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await Recognition(db).TurnOffAsync(Ana);

        Assert.False(await Recognition(db).TurnOnAsync());
        Assert.False((await FaceRecognition.ReadAsync(db)).On);
        // A sweep recorded for an earlier erase does not count.
        var erasedAt = (await FaceRecognition.ReadAsync(db)).ErasedAt!.Value;
        Assert.False(await Recognition(db).MarkSweptAsync(erasedAt.AddDays(-1), erasedAt));
        Assert.False(await Recognition(db).TurnOnAsync());
    }

    [Fact]
    public async Task Turning_recognition_off_waits_for_an_import_that_holds_the_people_lock()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        await using var importing = Context(fixture.ConnectionStringFor(db));
        await using var transaction = await importing.Database.BeginTransactionAsync();
        await FaceAssigner.LockAsync(importing, CancellationToken.None);

        await using var editing = Context(fixture.ConnectionStringFor(db));
        var turnOff = new FaceRecognition(editing, TimeProvider.System).TurnOffAsync(Ana);
        await Task.Delay(500);
        Assert.False(turnOff.IsCompleted);

        await transaction.CommitAsync();
        Assert.Equal(0, (await turnOff)!.Faces);
    }

    [Fact]
    public async Task Deleting_a_person_s_face_data_keeps_the_photos_and_a_re_import_does_not_bring_it_back()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var ana = SyntheticFaces.Unit(seed: 1);
        var bruno = SyntheticFaces.Unit(seed: 3);
        var photo1 = FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, ana), FaceImportTests.Face([0.6, 0.2, 0.3, 0.4], bruno));
        var photo2 = FaceImportTests.WithFaces(2, FaceImportTests.Face(s_box, SyntheticFaces.Near(ana, seed: 2)));
        await PutAsync(container, photo1);
        await PutAsync(container, photo2);
        await ImportAsync(db, container);
        var anaId = await db.Faces.Where(f => f.AssetId == photo2.Id).Select(f => f.PersonId!.Value).SingleAsync();

        var deletion = await Recognition(db).DeletePersonAsync(anaId, Ana);
        // Another computer uploads the same photos again: their manifests still list Ana's faces.
        await PutAsync(container, photo1 with { Sources = [.. photo1.Sources, photo1.Sources[0] with { Name = "HD do Bruno" }] });
        await PutAsync(container, photo2 with { Sources = [.. photo2.Sources, photo2.Sources[0] with { Name = "HD do Bruno" }] });
        var reimport = await ImportAsync(db, container);

        Assert.Equal((FaceDataDeletionScopes.Person, 2, 1), (deletion!.Scope, deletion.Faces, deletion.People));
        Assert.Equal(2, reimport.Imported);
        Assert.Equal(2, await db.Assets.CountAsync());
        Assert.False(await db.People.AnyAsync(p => p.Id == anaId));
        var left = await db.Faces.AsNoTracking().SingleAsync();
        Assert.Equal((photo1.Id, 0.6), (left.AssetId, left.Box[0]));
        Assert.Equal(1, (await db.People.AsNoTracking().SingleAsync()).FaceCount);
        Assert.Equal(2, await db.ErasedFaces.CountAsync());
        Assert.Null(await Recognition(db).DeletePersonAsync(anaId, Ana));
    }

    [Fact]
    public async Task Ignoring_a_strangers_face_deletes_it_recomputes_the_people_and_a_re_import_does_not_bring_it_back()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var ana = SyntheticFaces.Unit(seed: 1);
        var bruno = SyntheticFaces.Unit(seed: 3);
        var photo1 = FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, ana), FaceImportTests.Face([0.6, 0.2, 0.3, 0.4], bruno));
        var photo2 = FaceImportTests.WithFaces(2, FaceImportTests.Face(s_box, SyntheticFaces.Near(ana, seed: 2)));
        await PutAsync(container, photo1);
        await PutAsync(container, photo2);
        await ImportAsync(db, container);
        var brunoFace = await db.Faces.SingleAsync(f => f.AssetId == photo1.Id && f.Box[0] == 0.6);
        var anaFaceOnPhoto2 = await db.Faces.AsNoTracking().SingleAsync(f => f.AssetId == photo2.Id);

        var deletion = await Recognition(db).IgnoreFacesAsync(photo1.Id, brunoFace.Id, Ana);

        Assert.Equal((FaceDataDeletionScopes.Ignore, 1, 1), (deletion!.Scope, deletion.Faces, deletion.People));
        // Bruno was unnamed with that one face: gone. Ana's person keeps both faces.
        var people = await db.People.AsNoTracking().ToListAsync();
        Assert.Equal(2, Assert.Single(people).FaceCount);
        Assert.Equal(2, await db.Faces.CountAsync());
        Assert.Equal(1, await db.ErasedFaces.CountAsync());
        // The manifests still list the face; the same box is not found again.
        await PutAsync(container, photo1 with { Sources = [.. photo1.Sources, photo1.Sources[0] with { Name = "HD do Bruno" }] });
        await ImportAsync(db, container);
        Assert.Equal(2, await db.Faces.CountAsync());
        Assert.Single(await db.People.ToListAsync());

        // Ana's person is recomputed from what is left when one of her two faces goes.
        var second = await Recognition(db).IgnoreFacesAsync(photo2.Id, anaFaceOnPhoto2.Id, Ana);

        Assert.Equal((1, 0), (second!.Faces, second.People));
        var left = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal(1, left.FaceCount);
        Assert.True(Cosine(left.Centroid!.ToArray(), ana) > 0.999);
    }

    [Fact]
    public async Task Ignoring_the_unnamed_faces_of_a_photo_never_touches_a_named_person_s_face()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var photo = FaceImportTests.WithFaces(
            1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 1)), FaceImportTests.Face([0.6, 0.2, 0.3, 0.4], SyntheticFaces.Unit(seed: 3)));
        await PutAsync(container, photo);
        await ImportAsync(db, container);
        var named = await db.Faces.AsNoTracking().SingleAsync(f => f.AssetId == photo.Id && f.Box[0] == s_box[0]);
        Assert.Equal(CurationOutcome.Done, await new PeopleCuration(db).RenameAsync(named.PersonId!.Value, "Ana"));

        // Asked for the named face by id, and then for "all the unnamed" ones.
        Assert.Null(await Recognition(db).IgnoreFacesAsync(photo.Id, named.Id, Ana));
        var all = await Recognition(db).IgnoreFacesAsync(photo.Id, null, Ana);

        Assert.Equal((1, 1), (all!.Faces, all.People));
        Assert.Equal(named.Id, (await db.Faces.AsNoTracking().SingleAsync()).Id);
        Assert.Equal("Ana", (await db.People.AsNoTracking().SingleAsync()).Name);
        Assert.Null(await Recognition(db).IgnoreFacesAsync(photo.Id, null, Ana));
    }

    [Fact]
    public async Task Ignoring_a_person_refuses_a_named_one_and_remembers_every_box_of_an_unnamed_one()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var ana = SyntheticFaces.Unit(seed: 1);
        var photo1 = FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, ana));
        var photo2 = FaceImportTests.WithFaces(2, FaceImportTests.Face(s_box, SyntheticFaces.Near(ana, seed: 2)));
        await PutAsync(container, photo1);
        await PutAsync(container, photo2);
        await ImportAsync(db, container);
        var person = await db.People.SingleAsync();
        // One of the two faces was excluded by the family: it is not counted, but it is theirs to be rid of.
        var excluded = await db.Faces.FirstAsync(f => f.AssetId == photo2.Id);
        excluded.Assignment = FaceAssignments.Excluded;
        await db.SaveChangesAsync();
        Assert.Equal(CurationOutcome.Done, await new PeopleCuration(db).RenameAsync(person.Id, "Ana"));
        Assert.Null(await Recognition(db).IgnorePersonAsync(person.Id, Ana));
        Assert.Equal(CurationOutcome.Done, await new PeopleCuration(db).RenameAsync(person.Id, null));

        var deletion = await Recognition(db).IgnorePersonAsync(person.Id, Ana);

        Assert.Equal((FaceDataDeletionScopes.Ignore, 2, 1), (deletion!.Scope, deletion.Faces, deletion.People));
        Assert.Equal(0, await db.Faces.CountAsync());
        Assert.Equal(2, await db.ErasedFaces.CountAsync());
        Assert.Equal(2, await db.Assets.CountAsync());
        Assert.Null(await Recognition(db).IgnorePersonAsync(person.Id, Ana));
    }

    [Fact]
    public async Task Ignoring_the_unnamed_people_with_few_faces_checks_the_number_and_leaves_everyone_else()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        for (var n = 1; n <= 3; n++)
        {
            await PutAsync(container, FaceImportTests.WithFaces(n));
        }

        await ImportAsync(db, container);
        var one = new Person { FaceCount = 1 };
        var two = new Person { FaceCount = 2 };
        var three = new Person { FaceCount = 3 };
        var named = new Person { Name = "Carla", FaceCount = 1 };
        var linked = new Person { UserEmail = Ana, FaceCount = 1 };
        db.People.AddRange(one, two, three, named, linked);
        await db.SaveChangesAsync();
        var assets = await db.Assets.OrderBy(a => a.Id).Select(a => a.Id).ToListAsync();
        Face Row(Person person, string asset, double left) => new()
        {
            AssetId = asset,
            Box = [left, 0.1, 0.2, 0.2],
            Score = 0.9,
            Embedding = new Pgvector.Vector(SyntheticFaces.Unit(seed: (int)(left * 100))),
            Assignment = FaceAssignments.Auto,
            PersonId = person.Id,
        };
        db.Faces.AddRange(
            Row(one, assets[0], 0.1), Row(two, assets[0], 0.3), Row(two, assets[1], 0.3), Row(three, assets[0], 0.5), Row(three, assets[1], 0.5),
            Row(three, assets[2], 0.5), Row(named, assets[2], 0.7), Row(linked, assets[1], 0.7));
        await db.SaveChangesAsync();

        var wrong = await Recognition(db).IgnoreUnnamedPeopleAsync(FaceRecognition.FewFaces, 3, Ana);
        Assert.True(wrong.Refused);
        Assert.Equal(5, await db.People.CountAsync());

        var done = await Recognition(db).IgnoreUnnamedPeopleAsync(FaceRecognition.FewFaces, 2, Ana);

        Assert.Equal((3, 2, false), (done.Faces, done.People, done.Refused));
        Assert.Equal(["Carla", null, null], (await db.People.AsNoTracking().OrderBy(p => p.Name == null).ThenBy(p => p.Id).Select(p => p.Name).ToListAsync()).Cast<string?>());
        Assert.Equal(5, await db.Faces.CountAsync());
        Assert.Equal(3, await db.ErasedFaces.CountAsync());
        Assert.Equal(3, await db.Assets.CountAsync());
        var recorded = await db.FaceDataDeletions.AsNoTracking().SingleAsync();
        Assert.Equal((FaceDataDeletionScopes.Ignore, 3, 2, Ana), (recorded.Scope, recorded.Faces, recorded.People, recorded.DeletedBy));
        Assert.True((await Recognition(db).IgnoreUnnamedPeopleAsync(FaceRecognition.FewFaces, 0, Ana)).Refused);
    }

    [Fact]
    public async Task Sweeps_the_erase_into_every_stored_manifest_then_only_into_those_written_since()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var old = FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 1)));
        await PutAsync(container, old);
        await PutAsync(container, FaceImportTests.WithFaces(2) with { FacesProcessed = false, FacesVersion = null });
        await Recognition(db).TurnOffAsync(Ana);
        var erasedAt = (await FaceRecognition.ReadAsync(db)).ErasedAt!.Value;

        var first = await SweepAsync(db, container);

        Assert.Equal((true, 2, 0), (first.Ran, first.Erased, first.Unchanged));
        Assert.Empty(first.Problems);
        var stored = await StoredAsync(container, old.Id);
        Assert.Empty(stored.Faces);
        Assert.Equal(erasedAt, stored.FacesErasedAt);
        var state = await FaceRecognition.ReadAsync(db);
        Assert.False(state.ManifestsPending);
        Assert.True(await Recognition(db).TurnOnAsync());

        // An old work folder uploads a new photo, with faces found before the erase, straight into a new blob.
        var late = FaceImportTests.WithFaces(3, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 4))) with { FacesDetectedAt = erasedAt.AddDays(-1) };
        await PutAsync(container, late);
        var second = await SweepAsync(db, container);

        Assert.Empty((await StoredAsync(container, late.Id)).Faces);
        Assert.True(second.Erased >= 1);
        Assert.Equal(3, second.Erased + second.Unchanged);
    }

    // The sweep skips a blob only if it was written before the previous pass: LastModified < since, so a blob written at
    // that very moment is swept (a boundary that, wrong, leaves a person's face in storage after they asked for its erase).
    [Fact]
    public async Task Visits_the_blobs_written_at_and_after_the_previous_pass_and_skips_those_before()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var clock = new SettableClock(DateTimeOffset.UtcNow);
        var memory = new InMemoryBlobStore(clock);
        memory.CreateContainer(BlobLayout.ManifestsContainer);
        await Recognition(db).TurnOffAsync(Ana);
        var blobs = new ManifestFacesSweep(db, memory, new ManifestStore(new ManifestValidator(), memory), clock, NullLogger.Instance);
        var first = await blobs.RunAsync(BlobLayout.ManifestsContainer, CancellationToken.None);
        Assert.True(first.Recorded);
        var since = (await FaceRecognition.ReadAsync(db)).SweptThrough!.Value;

        var times = new Dictionary<int, DateTimeOffset>
        {
            [1] = since - TimeSpan.FromSeconds(1),
            [2] = since,
            [3] = since + TimeSpan.FromSeconds(1),
        };
        foreach (var (n, time) in times)
        {
            clock.Now = time;
            var manifest = FaceImportTests.WithFaces(n, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: n)));
            await memory.WriteAsync(
                BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(manifest.Id), BinaryData.FromString(ManifestJson.Serialize(manifest)),
                "application/json", BlobWriteCondition.Always);
        }

        clock.Now = since + TimeSpan.FromMinutes(30);
        var second = await blobs.RunAsync(BlobLayout.ManifestsContainer, CancellationToken.None);

        Assert.Equal(2, second.Erased);
        Assert.NotEmpty(await StoredFacesAsync(memory, 1));
        Assert.Empty(await StoredFacesAsync(memory, 2));
        Assert.Empty(await StoredFacesAsync(memory, 3));
    }

    private static async Task<IReadOnlyList<ManifestFace>> StoredFacesAsync(InMemoryBlobStore store, int n)
    {
        var id = FaceImportTests.WithFaces(n).Id;
        var blob = await store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(id));
        return ManifestJson.Deserialize(blob!.Content.ToString()).Faces;
    }

    [Fact]
    public async Task Turning_recognition_off_again_with_nothing_left_changes_and_records_nothing()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var clock = new SettableClock(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));
        Assert.NotNull(await new FaceRecognition(db, clock).TurnOffAsync(Ana));
        clock.Now += TimeSpan.FromHours(1);

        var again = await new FaceRecognition(db, clock).TurnOffAsync("bruno@example.com");

        Assert.Null(again);
        Assert.Equal(1, await db.FaceDataDeletions.CountAsync());
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero), (await FaceRecognition.ReadAsync(db)).ErasedAt);
    }

    [Fact]
    public async Task Takes_a_computer_s_clock_into_account_when_recognition_is_back_on()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var erasedAt = new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
        var clock = new SettableClock(erasedAt);
        await new FaceRecognition(db, clock).TurnOffAsync(Ana);
        Assert.True(await new FaceRecognition(db, clock).MarkSweptAsync(erasedAt, erasedAt.AddMinutes(30)));
        clock.Now = erasedAt.AddHours(1);
        Assert.True(await new FaceRecognition(db, clock).TurnOnAsync());
        var enabledAt = clock.Now;
        Assert.Equal(enabledAt, (await FaceRecognition.ReadAsync(db)).EnabledAt);
        // A computer ten minutes ahead stamps faces found just before the erase after it: found while recognition was
        // off, so not imported.
        var ahead = FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 1))) with { FacesDetectedAt = erasedAt.AddMinutes(2) };
        // A computer ten minutes behind stamps faces found five minutes after switching back on before it: imported.
        var behind = FaceImportTests.WithFaces(2, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 3))) with { FacesDetectedAt = enabledAt.AddMinutes(-5) };
        // Faces found twenty minutes before switching back on, while it was off: not imported.
        var whileOff = FaceImportTests.WithFaces(3, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 4))) with { FacesDetectedAt = enabledAt.AddMinutes(-20) };
        foreach (var manifest in new[] { ahead, behind, whileOff })
        {
            await PutAsync(container, manifest);
        }

        await ImportAsync(db, container);

        Assert.Equal([behind.Id], await db.Faces.Select(f => f.AssetId).ToListAsync());
    }

    [Fact]
    public async Task Resumes_a_stopped_sweep_and_allows_turning_back_on_only_once_every_manifest_is_erased()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var manifests = Enumerable.Range(1, 7)
            .Select(n => FaceImportTests.WithFaces(n, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: n))))
            .ToList();
        foreach (var manifest in manifests)
        {
            await PutAsync(container, manifest);
        }

        await Recognition(db).TurnOffAsync(Ana);
        // Two blobs a page, and a run stops after its first page, as the job's budget or timeout would stop it.
        var stopping = new SweepOptions(Concurrency: 4, PageSize: 2, Budget: TimeSpan.Zero);

        var runs = new List<SweepResult> { await SweepAsync(db, container, stopping) };
        Assert.False(runs[0].Finished);
        Assert.Equal(2, runs[0].Erased);
        Assert.NotNull(await FaceRecognition.ReadSweepAsync(db));
        Assert.False(await Recognition(db).TurnOnAsync());
        while (!runs[^1].Finished)
        {
            Assert.True(runs.Count < 10, "The sweep does not finish.");
            Assert.False(await Recognition(db).TurnOnAsync());
            runs.Add(await SweepAsync(db, container, stopping));
        }

        Assert.Equal(4, runs.Count);
        Assert.Equal(7, runs.Sum(r => r.Erased));
        Assert.All(runs, r => Assert.Equal(0, r.Unchanged));
        foreach (var manifest in manifests)
        {
            Assert.Empty((await StoredAsync(container, manifest.Id)).Faces);
        }

        Assert.Null(await FaceRecognition.ReadSweepAsync(db));
        Assert.True(await Recognition(db).TurnOnAsync());
    }

    [Fact]
    public async Task Keeps_the_pass_start_across_runs_so_a_blob_written_behind_the_checkpoint_is_erased_by_the_next_pass()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAllAsync(container, Enumerable.Range(2, 5));
        await Recognition(db).TurnOffAsync(Ana);
        // The job's clock moves an hour between runs, as the hourly schedule does.
        var clock = new SettableClock(DateTimeOffset.UtcNow);
        var startedAt = clock.Now;

        var first = await SweepAsync(db, container, s_onePageARun, clock);
        Assert.False(first.Finished);
        // An old work folder uploads a photo whose blob sorts before the page already swept.
        var behind = FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 1)));
        await PutAsync(container, behind);
        clock.Now += TimeSpan.FromHours(1);
        var rest = await SweepUntilFinishedAsync(db, container, s_onePageARun, clock);

        Assert.True(rest[^1].Recorded);
        Assert.Single((await StoredAsync(container, behind.Id)).Faces);
        Assert.Equal(startedAt - ManifestFacesSweep.Margin, (await FaceRecognition.ReadAsync(db)).SweptThrough);
        clock.Now += TimeSpan.FromHours(1);
        var next = await SweepUntilFinishedAsync(db, container, s_onePageARun, clock);

        Assert.Empty((await StoredAsync(container, behind.Id)).Faces);
        Assert.True(next[^1].Recorded);
    }

    [Fact]
    public async Task Records_nothing_and_keeps_recognition_off_when_an_earlier_run_of_the_pass_had_a_problem()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        // Sorts first, so the first run meets it.
        var broken = container.GetBlobClient(BlobLayout.ManifestBlobName(new string('0', 64)));
        await broken.UploadAsync(BinaryData.FromString("{ \"id\": 42 }"));
        await PutAllAsync(container, Enumerable.Range(1, 4));
        await Recognition(db).TurnOffAsync(Ana);

        var first = await SweepAsync(db, container, s_onePageARun);
        Assert.Single(first.Problems);
        // Fixed before the next run: the rest of the pass is clean.
        await broken.DeleteAsync();
        var rest = await SweepUntilFinishedAsync(db, container, s_onePageARun);

        Assert.All(rest, run => Assert.Empty(run.Problems));
        Assert.False(rest[^1].Recorded);
        Assert.True((await FaceRecognition.ReadAsync(db)).ManifestsPending);
        Assert.False(await Recognition(db).TurnOnAsync());
        // The next pass starts over and is clean.
        var again = await SweepUntilFinishedAsync(db, container, s_onePageARun);
        Assert.True(again[^1].Recorded);
        Assert.True(await Recognition(db).TurnOnAsync());
    }

    [Fact]
    public async Task Does_nothing_while_another_sweep_holds_the_lock()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAllAsync(container, [1]);
        await Recognition(db).TurnOffAsync(Ana);
        await using var other = Context(fixture.ConnectionStringFor(db));
        await other.Database.OpenConnectionAsync();
        await other.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({ManifestFacesSweep.SweepLockKey})");

        var result = await SweepAsync(db, container);

        Assert.False(result.Ran);
        Assert.Single((await StoredAsync(container, FaceImportTests.WithFaces(1).Id)).Faces);
        Assert.True((await FaceRecognition.ReadAsync(db)).ManifestsPending);
    }

    [Fact]
    public async Task Stops_the_pass_when_recognition_is_turned_off_again_meanwhile()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAllAsync(container, Enumerable.Range(1, 5));
        await Recognition(db).TurnOffAsync(Ana);
        var later = (await FaceRecognition.ReadAsync(db)).ErasedAt!.Value.AddMinutes(5);
        // Another erase lands while the first page is being swept.
        var interfering = new InterferingStore(TestBlobStore.Of(container), async () =>
        {
            await using var other = Context(fixture.ConnectionStringFor(db));
            await other.Settings.Where(x => x.Key == FaceRecognition.ErasedAtKey)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.Value, later.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
        });

        var result = await SweepAsync(db, container, new SweepOptions(Concurrency: 4, PageSize: 2, Budget: TimeSpan.FromHours(1)), store: interfering);

        Assert.False(result.Finished);
        Assert.Equal(2, result.Erased);
        Assert.Null(await FaceRecognition.ReadSweepAsync(db));
        Assert.Null((await FaceRecognition.ReadAsync(db)).SweptThrough);
    }

    [Fact]
    public async Task Ignores_a_checkpoint_left_by_another_erase()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAllAsync(container, Enumerable.Range(1, 3));
        await Recognition(db).TurnOffAsync(Ana);
        var erasedAt = (await FaceRecognition.ReadAsync(db)).ErasedAt!.Value;
        db.Settings.Add(new Setting
        {
            Key = FaceRecognition.SweepKey,
            Value = System.Text.Json.JsonSerializer.Serialize(new SweepCheckpoint(erasedAt.AddDays(-1), null, erasedAt.AddDays(-1), "not-a-token", false)),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await SweepAsync(db, container);

        Assert.True(result.Recorded);
        Assert.Equal(3, result.Erased);
    }

    [Fact]
    public async Task Ignores_faces_said_to_be_found_after_their_manifest_was_uploaded()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        // A computer a day ahead when it ran process, corrected before upload.
        var ahead = FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 1))) with
        {
            FacesDetectedAt = DateTimeOffset.UtcNow.AddDays(1),
        };
        var fine = FaceImportTests.WithFaces(2, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 2))) with
        {
            FacesDetectedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        await PutAsync(container, ahead);
        await PutAsync(container, fine);

        var result = await ImportAsync(db, container);

        Assert.Equal(2, result.Imported);
        Assert.Equal([fine.Id], await db.Faces.Select(f => f.AssetId).ToListAsync());
        Assert.Contains(result.Warnings, w => w.Reason.Contains("a clock ahead", StringComparison.Ordinal));

        // And the sweep erases them from the blob, though their time is after the erase.
        await Recognition(db).TurnOffAsync(Ana);
        await SweepAsync(db, container);
        Assert.Empty((await StoredAsync(container, ahead.Id)).Faces);
    }

    [Fact]
    public async Task Leaves_the_sweep_pending_while_a_stored_manifest_cannot_be_read()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await container.GetBlobClient(BlobLayout.ManifestBlobName(new string('e', 64))).UploadAsync(BinaryData.FromString("{ \"id\": 42 }"));
        await Recognition(db).TurnOffAsync(Ana);

        var result = await SweepAsync(db, container);

        Assert.Equal([BlobLayout.ManifestBlobName(new string('e', 64))], result.Problems);
        Assert.True((await FaceRecognition.ReadAsync(db)).ManifestsPending);
    }

    [Fact]
    public async Task Sweeps_nothing_while_recognition_was_never_turned_off()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var manifest = FaceImportTests.WithFaces(1, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: 1)));
        await PutAsync(container, manifest);

        var result = await SweepAsync(db, container);

        Assert.False(result.Ran);
        Assert.Single((await StoredAsync(container, manifest.Id)).Faces);
        Assert.Equal(FaceRecognitionState.Default, await FaceRecognition.ReadAsync(db));
    }

    private static FaceRecognition Recognition(CatalogDbContext db) => new(db, TimeProvider.System);

    private static Task<SweepResult> SweepAsync(
        CatalogDbContext db, BlobContainerClient container, SweepOptions? options = null, TimeProvider? clock = null, IBlobStore? store = null) =>
        new ManifestFacesSweep(
                db, TestBlobStore.Of(container), new ManifestStore(new ManifestValidator(), store ?? TestBlobStore.Of(container)),
                clock ?? TimeProvider.System, NullLogger.Instance, options)
            .RunAsync(container.Name, CancellationToken.None);

    // Runs a stopping sweep until its pass finishes, at most ten times.
    private static async Task<List<SweepResult>> SweepUntilFinishedAsync(
        CatalogDbContext db, BlobContainerClient container, SweepOptions options, TimeProvider? clock = null)
    {
        var runs = new List<SweepResult>();
        do
        {
            Assert.True(runs.Count < 10, "The sweep does not finish.");
            runs.Add(await SweepAsync(db, container, options, clock));
        }
        while (!runs[^1].Finished);
        return runs;
    }

    private static readonly SweepOptions s_onePageARun = new(Concurrency: 4, PageSize: 2, Budget: TimeSpan.Zero);

    private static async Task PutAllAsync(BlobContainerClient container, IEnumerable<int> ids)
    {
        foreach (var n in ids)
        {
            await PutAsync(container, FaceImportTests.WithFaces(n, FaceImportTests.Face(s_box, SyntheticFaces.Unit(seed: n))));
        }
    }

    /// <summary>The blob store the manifest writer uses, which runs <paramref name="interfere"/> once, the first time a blob is read.</summary>
    private sealed class InterferingStore(IBlobStore inner, Func<Task> interfere) : DelegatingBlobStore(inner)
    {
        private int _done;

        public override async Task<BlobContent?> ReadAsync(string container, string name, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
            {
                await interfere();
            }

            return await base.ReadAsync(container, name, cancellationToken);
        }
    }

    private sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task<Manifest> StoredAsync(BlobContainerClient container, string id) =>
        ManifestJson.Deserialize((await container.GetBlobClient(BlobLayout.ManifestBlobName(id)).DownloadContentAsync()).Value.Content.ToString());

    private static CatalogDbContext Context(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContext.Configure(options, connectionString);
        return new CatalogDbContext(options.Options);
    }

    private static Task<ImportResult> ImportAsync(CatalogDbContext db, BlobContainerClient container) =>
        new ManifestImporter(db, TestBlobStore.Of(container), new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance)
            .ImportOnceAsync(container.Name, CancellationToken.None);

    private static async Task PutAsync(BlobContainerClient container, Manifest manifest) =>
        await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id))
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(manifest)), overwrite: true);

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return dot / Math.Sqrt(na * nb);
    }
}
