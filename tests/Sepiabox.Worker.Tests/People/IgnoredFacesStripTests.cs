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
/// Taking the faces the family deleted out of the stored manifests (#111, D-121): which faces go, that nothing else changes, that a second
/// run writes nothing, and that a manifest an upload put them back in is stripped again. Synthetic vectors and boxes only.
/// </summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class IgnoredFacesStripTests(CatalogFixture fixture)
{
    // Boxes are [left, top, width, height]. A face the family deleted, one that overlaps it a lot, one that touches it a little, and a stranger far away.
    private static readonly double[] s_deleted = [0.10, 0.10, 0.20, 0.20];
    private static readonly double[] s_sameFace = [0.11, 0.11, 0.20, 0.20];
    private static readonly double[] s_neighbour = [0.25, 0.10, 0.20, 0.20];
    private static readonly double[] s_elsewhere = [0.70, 0.60, 0.20, 0.20];

    [Fact]
    public async Task Removes_the_face_that_overlaps_a_deleted_box_with_its_embedding_and_leaves_the_others_and_the_rest_of_the_manifest_as_they_were()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        var manifest = await PutAsync(store, 1, (s_sameFace, 1), (s_neighbour, 2), (s_elsewhere, 3));
        await ForgetAsync(db, manifest.Id, s_deleted);

        var result = await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);

        Assert.Equal((1, 1, 0, 0), (result.Assets, result.Stripped, result.Unchanged, result.Problems.Count));
        var stored = await StoredAsync(store, manifest.Id);
        Assert.Equal([s_neighbour, s_elsewhere], stored.Faces.Select(f => f.Box.ToArray()));
        Assert.DoesNotContain(manifest.Faces[0].Embedding, ManifestJson.Serialize(stored), StringComparison.Ordinal);
        // Nothing but the faces changed: what the asset is, where it came from, and how its faces were found.
        Assert.Equal(ManifestJson.Serialize(manifest with { Faces = stored.Faces }), ManifestJson.Serialize(stored));
        Assert.Equal((manifest.FacesVersion, manifest.FacesProcessed), (stored.FacesVersion, stored.FacesProcessed));
    }

    [Theory]
    [InlineData(0.15, 0.10, true)]
    [InlineData(0.10, 0.10, true)]
    [InlineData(0.30, 0.10, false)]
    [InlineData(0.24, 0.10, false)]
    public async Task Takes_a_face_at_an_overlap_of_a_half_or_more_and_not_less(double left, double top, bool removed)
    {
        // The deleted box is [0.10, 0.10, 0.20, 0.20]; a face shifted by 0.05 overlaps it by 0.15 x 0.20 over 0.5 x ... : 60%; by 0.14: 0.06 x 0.20, under half.
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        double[] face = [left, top, 0.20, 0.20];
        var manifest = await PutAsync(store, 2, (face, 1));
        await ForgetAsync(db, manifest.Id, s_deleted);
        var iou = FaceMath.IoU(face, s_deleted);

        await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);

        Assert.Equal(removed, iou >= FaceAssigner.SameBoxIoU);
        Assert.Equal(removed ? 0 : 1, (await StoredAsync(store, manifest.Id)).Faces.Count);
    }

    [Fact]
    public async Task A_second_run_writes_nothing_and_leaves_the_blob_as_it_was()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        var manifest = await PutAsync(store, 3, (s_sameFace, 1), (s_elsewhere, 2));
        await ForgetAsync(db, manifest.Id, s_deleted);
        await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);
        var after = await store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(manifest.Id));

        var again = await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);

        Assert.Equal((0, 1), (again.Stripped, again.Unchanged));
        var still = await store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(manifest.Id));
        Assert.Equal(after!.Version, still!.Version);
        Assert.Equal(after.Content.ToString(), still.Content.ToString());
    }

    [Fact]
    public async Task Leaves_every_other_manifest_alone_and_one_that_is_not_in_storage_or_never_looked_for_faces()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        var other = await PutAsync(store, 4, (s_sameFace, 1));
        var noFaces = FaceImportTests.WithFaces(5) with { FacesProcessed = false, FacesVersion = null };
        await store.WriteAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(noFaces.Id), BinaryData.FromString(ManifestJson.Serialize(noFaces)), "application/json", BlobWriteCondition.Always);
        var otherBefore = await store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(other.Id));
        var missing = FaceImportTests.WithFaces(6).Id;
        await ForgetAsync(db, noFaces.Id, s_deleted);
        await ForgetAsync(db, missing, s_deleted);

        var result = await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);

        Assert.Equal((2, 0, 2), (result.Assets, result.Stripped, result.Unchanged));
        Assert.Equal(otherBefore!.Version, (await store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(other.Id)))!.Version);
        Assert.Null(await store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(missing)));
    }

    [Fact]
    public async Task Does_nothing_while_no_face_was_deleted()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        var manifest = await PutAsync(store, 7, (s_sameFace, 1));

        var result = await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);

        Assert.Equal((0, 0, 0), (result.Assets, result.Stripped, result.Unchanged));
        Assert.Single((await StoredAsync(store, manifest.Id)).Faces);
    }

    [Fact]
    public async Task A_manifest_an_upload_put_the_deleted_faces_back_in_is_stripped_again_by_the_next_run()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        var manifest = await PutAsync(store, 8, (s_sameFace, 1), (s_elsewhere, 2));
        await ForgetAsync(db, manifest.Id, s_deleted);
        await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);

        // The same asset uploaded again from a computer that still has both faces: the merge keeps the side with more faces, so they return.
        await new ManifestStore(new ManifestValidator(), store).WriteMergedAsync(BlobLayout.ManifestsContainer, manifest, CancellationToken.None);
        Assert.Equal(2, (await StoredAsync(store, manifest.Id)).Faces.Count);
        var healed = await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);

        Assert.Equal(1, healed.Stripped);
        Assert.Equal([s_elsewhere], (await StoredAsync(store, manifest.Id)).Faces.Select(f => f.Box.ToArray()));
    }

    [Fact]
    public async Task A_re_import_of_the_stripped_manifest_does_not_bring_the_face_back_and_keeps_the_others()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        var manifest = await PutAsync(store, 9, (s_sameFace, 1), (s_elsewhere, 2));
        var importer = new ManifestImporter(db, store, new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance);
        await importer.ImportOneAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(manifest.Id), CancellationToken.None);
        Assert.Equal(2, await db.Faces.CountAsync());
        // What deleting the face data does in the catalog: the row goes and its box is remembered.
        await db.Faces.Where(f => f.Box[0] == s_sameFace[0]).ExecuteDeleteAsync();
        await ForgetAsync(db, manifest.Id, s_deleted);
        await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);

        var result = await importer
            .ImportOneAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(manifest.Id), CancellationToken.None);

        Assert.Equal(1, result.Imported);
        Assert.Equal([s_elsewhere], (await db.Faces.AsNoTracking().Select(f => f.Box).ToListAsync()).Select(b => b.ToArray()));
    }

    [Fact]
    public async Task A_manifest_that_cannot_be_read_is_a_problem_the_others_still_go_and_the_run_names_it()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        var good = await PutAsync(store, 10, (s_sameFace, 1));
        var badId = FaceImportTests.WithFaces(11).Id;
        await store.WriteAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(badId), BinaryData.FromString("{ \"id\": 42 }"), "application/json", BlobWriteCondition.Always);
        await ForgetAsync(db, good.Id, s_deleted);
        await ForgetAsync(db, badId, s_deleted);

        var result = await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);

        Assert.Equal(1, result.Stripped);
        Assert.Equal([badId], result.Problems);
        Assert.Empty((await StoredAsync(store, good.Id)).Faces);
    }

    [Fact]
    public async Task A_later_run_visits_only_the_assets_whose_boxes_were_remembered_since_the_last_complete_run_and_the_first_visits_all()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        var clock = new SettableClock(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var a = await PutAsync(store, 20, (s_sameFace, 1), (s_elsewhere, 2));
        await ForgetAsync(db, a.Id, s_deleted, clock.Now - TimeSpan.FromHours(1));
        var first = await Strip(db, store, clock).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);
        Assert.Equal((true, 1, 1), (first.Full, first.Assets, first.Stripped));
        Assert.Equal(clock.Now.UtcDateTime.ToString("O"), await SettingAsync(db, IgnoredFacesStrip.CheckpointKey));
        Assert.Equal(clock.Now.UtcDateTime.ToString("O"), await SettingAsync(db, IgnoredFacesStrip.FullPassKey));

        // Two hours later another person's face is deleted; an upload has put A's face back in the meantime.
        clock.Now += TimeSpan.FromHours(2);
        var b = await PutAsync(store, 21, (s_sameFace, 3), (s_elsewhere, 4));
        await ForgetAsync(db, b.Id, s_deleted, clock.Now);
        await new ManifestStore(new ManifestValidator(), store).WriteMergedAsync(BlobLayout.ManifestsContainer, a, CancellationToken.None);
        var second = await Strip(db, store, clock).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);

        Assert.Equal((false, 1, 1), (second.Full, second.Assets, second.Stripped));
        Assert.Single((await StoredAsync(store, b.Id)).Faces);
        // A was not visited: its restored face waits for the daily full pass.
        Assert.Equal(2, (await StoredAsync(store, a.Id)).Faces.Count);
    }

    [Fact]
    public async Task Once_a_day_a_run_visits_every_asset_with_boxes_and_heals_a_manifest_another_writer_put_the_faces_back_in()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        var clock = new SettableClock(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var a = await PutAsync(store, 22, (s_sameFace, 1), (s_elsewhere, 2));
        await ForgetAsync(db, a.Id, s_deleted, clock.Now - TimeSpan.FromHours(1));
        await Strip(db, store, clock).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);
        await new ManifestStore(new ManifestValidator(), store).WriteMergedAsync(BlobLayout.ManifestsContainer, a, CancellationToken.None);

        clock.Now += TimeSpan.FromHours(23);
        var notYet = await Strip(db, store, clock).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);
        clock.Now += TimeSpan.FromHours(2);
        var daily = await Strip(db, store, clock).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);

        Assert.Equal((false, 0), (notYet.Full, notYet.Assets));
        Assert.Equal((true, 1, 1), (daily.Full, daily.Assets, daily.Stripped));
        Assert.Equal([s_elsewhere], (await StoredAsync(store, a.Id)).Faces.Select(f => f.Box.ToArray()));
        Assert.Equal(clock.Now.UtcDateTime.ToString("O"), await SettingAsync(db, IgnoredFacesStrip.FullPassKey));
    }

    [Fact]
    public async Task Boxes_remembered_within_the_clock_margin_before_the_checkpoint_are_visited_and_older_ones_are_not()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        var clock = new SettableClock(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        await Strip(db, store, clock).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);
        clock.Now += TimeSpan.FromHours(1);
        var checkpoint = clock.Now;
        await Strip(db, store, clock).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);
        var near = await PutAsync(store, 23, (s_sameFace, 1));
        var old = await PutAsync(store, 24, (s_sameFace, 2));
        await ForgetAsync(db, near.Id, s_deleted, checkpoint - TimeSpan.FromMinutes(5));
        await ForgetAsync(db, old.Id, s_deleted, checkpoint - TimeSpan.FromMinutes(11));
        clock.Now += TimeSpan.FromMinutes(30);

        var result = await Strip(db, store, clock).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);

        Assert.Equal((false, 1, 1), (result.Full, result.Assets, result.Stripped));
        Assert.Empty((await StoredAsync(store, near.Id)).Faces);
        Assert.Single((await StoredAsync(store, old.Id)).Faces);
    }

    [Fact]
    public async Task A_run_with_a_problem_leaves_the_checkpoint_where_it_was_so_the_next_run_visits_the_same_assets_again()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        var clock = new SettableClock(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        await Strip(db, store, clock).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);
        var checkpoint = await SettingAsync(db, IgnoredFacesStrip.CheckpointKey);
        clock.Now += TimeSpan.FromHours(1);
        var badId = FaceImportTests.WithFaces(25).Id;
        await store.WriteAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(badId), BinaryData.FromString("{ \"id\": 42 }"), "application/json", BlobWriteCondition.Always);
        await ForgetAsync(db, badId, s_deleted, clock.Now);

        var failed = await Strip(db, store, clock).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);
        clock.Now += TimeSpan.FromHours(3);
        var again = await Strip(db, store, clock).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);

        Assert.Equal([badId], failed.Problems);
        Assert.Equal(checkpoint, await SettingAsync(db, IgnoredFacesStrip.CheckpointKey));
        Assert.Equal([badId], again.Problems);
        Assert.Equal((false, 1), (again.Full, again.Assets));
    }

    [Fact]
    public async Task A_deadline_in_the_past_visits_nothing_writes_nothing_and_moves_neither_the_checkpoint_nor_the_full_pass()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        var clock = new SettableClock(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var manifest = await PutAsync(store, 30, (s_sameFace, 1), (s_elsewhere, 2));
        await ForgetAsync(db, manifest.Id, s_deleted, clock.Now - TimeSpan.FromHours(1));

        var stopped = await Strip(db, store, clock).RunAsync(BlobLayout.ManifestsContainer, clock.Now - TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal((0, 0, 0, false), (stopped.Assets, stopped.Stripped, stopped.Unchanged, stopped.Finished));
        Assert.Equal(2, (await StoredAsync(store, manifest.Id)).Faces.Count);
        Assert.Null(await SettingAsync(db, IgnoredFacesStrip.CheckpointKey));
        Assert.Null(await SettingAsync(db, IgnoredFacesStrip.FullPassKey));

        // The next run, in time, does the whole job and then records it.
        var next = await Strip(db, store, clock).RunAsync(BlobLayout.ManifestsContainer, clock.Now + TimeSpan.FromMinutes(28), CancellationToken.None);

        Assert.Equal((true, 1, 1, true), (next.Full, next.Assets, next.Stripped, next.Finished));
        Assert.NotNull(await SettingAsync(db, IgnoredFacesStrip.CheckpointKey));
        Assert.NotNull(await SettingAsync(db, IgnoredFacesStrip.FullPassKey));
    }

    [Fact]
    public async Task Strips_many_manifests_at_once_and_every_one_goes()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        var ids = new List<string>();
        for (var n = 100; n < 140; n++)
        {
            var manifest = await PutAsync(store, n, (s_sameFace, n), (s_elsewhere, n + 1000));
            await ForgetAsync(db, manifest.Id, s_deleted);
            ids.Add(manifest.Id);
        }

        var result = await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, null, CancellationToken.None);

        Assert.Equal((40, 40, 0), (result.Assets, result.Stripped, result.Problems.Count));
        foreach (var id in ids)
        {
            Assert.Single((await StoredAsync(store, id)).Faces);
        }
    }

    private static Task<string?> SettingAsync(CatalogDbContext db, string key) =>
        db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => (string?)s.Value).SingleOrDefaultAsync();

    private static InMemoryBlobStore Store()
    {
        var store = new InMemoryBlobStore();
        store.CreateContainer(BlobLayout.ManifestsContainer);
        return store;
    }

    private static IgnoredFacesStrip Strip(CatalogDbContext db, InMemoryBlobStore store, TimeProvider? clock = null) =>
        new(db, new ManifestStore(new ManifestValidator(), store), clock ?? TimeProvider.System, NullLogger.Instance);

    private sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task<Manifest> PutAsync(InMemoryBlobStore store, int n, params (double[] Box, int Seed)[] faces)
    {
        var manifest = FaceImportTests.WithFaces(n, [.. faces.Select(f => FaceImportTests.Face(f.Box, SyntheticFaces.Unit(seed: f.Seed)))]);
        await store.WriteAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(manifest.Id), BinaryData.FromString(ManifestJson.Serialize(manifest)), "application/json", BlobWriteCondition.Always);
        return manifest;
    }

    private static async Task<Manifest> StoredAsync(InMemoryBlobStore store, string id) =>
        ManifestJson.Deserialize((await store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(id)))!.Content.ToString());

    // What deleting a person's face data leaves in the catalog: the box, never the embedding.
    private static async Task ForgetAsync(CatalogDbContext db, string assetId, double[] box, DateTimeOffset? at = null)
    {
        // erased_faces points at the asset, so the asset is in the catalog (the manifest need not be in storage).
        if (!await db.Assets.AnyAsync(a => a.Id == assetId))
        {
            db.Assets.Add(new Asset { Id = assetId, Kind = "photo", ContentType = "image/jpeg", SizeBytes = 1, ImportedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        }

        db.ErasedFaces.Add(new ErasedFace { AssetId = assetId, Box = box, ErasedAt = at ?? DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }
}
