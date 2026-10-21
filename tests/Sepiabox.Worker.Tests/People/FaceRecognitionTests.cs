using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Catalog.People;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
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

    private static Task<SweepResult> SweepAsync(CatalogDbContext db, BlobContainerClient container, SweepOptions? options = null) =>
        new ManifestFacesSweep(db, new ManifestStore(new ManifestValidator()), TimeProvider.System, NullLogger.Instance, options)
            .RunAsync(container, CancellationToken.None);

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
        new ManifestImporter(db, new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance)
            .ImportOnceAsync(container, CancellationToken.None);

    private static async Task PutAsync(BlobContainerClient container, Manifest manifest) =>
        await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id))
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(manifest)), overwrite: true);
}
