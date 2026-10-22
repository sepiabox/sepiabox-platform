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

        var result = await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, CancellationToken.None);

        Assert.Equal((1, 1, 0, 0), (result.Assets, result.Stripped, result.Unchanged, result.Problems.Count));
        var stored = await StoredAsync(store, manifest.Id);
        Assert.Equal([s_neighbour, s_elsewhere], stored.Faces.Select(f => f.Box.ToArray()));
        Assert.DoesNotContain(manifest.Faces[0].Embedding, ManifestJson.Serialize(stored), StringComparison.Ordinal);
        // Nothing but the faces changed: what the asset is, where it came from, and how its faces were found.
        Assert.Equal(manifest with { Faces = stored.Faces }, stored with { });
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

        await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, CancellationToken.None);

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
        await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, CancellationToken.None);
        var after = await store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(manifest.Id));

        var again = await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, CancellationToken.None);

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

        var result = await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, CancellationToken.None);

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

        var result = await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, CancellationToken.None);

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
        await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, CancellationToken.None);

        // The same asset uploaded again from a computer that still has both faces: the merge keeps the side with more faces, so they return.
        await new ManifestStore(new ManifestValidator(), store).WriteMergedAsync(BlobLayout.ManifestsContainer, manifest, CancellationToken.None);
        Assert.Equal(2, (await StoredAsync(store, manifest.Id)).Faces.Count);
        var healed = await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, CancellationToken.None);

        Assert.Equal(1, healed.Stripped);
        Assert.Equal([s_elsewhere], (await StoredAsync(store, manifest.Id)).Faces.Select(f => f.Box.ToArray()));
    }

    [Fact]
    public async Task A_re_import_of_the_stripped_manifest_does_not_bring_the_face_back_and_keeps_the_others()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var store = Store();
        var manifest = await PutAsync(store, 9, (s_sameFace, 1), (s_elsewhere, 2));
        await ForgetAsync(db, manifest.Id, s_deleted);
        await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, CancellationToken.None);

        var result = await new ManifestImporter(db, store, new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance)
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

        var result = await Strip(db, store).RunAsync(BlobLayout.ManifestsContainer, CancellationToken.None);

        Assert.Equal((1, [badId]), (result.Stripped, result.Problems.ToArray()));
        Assert.Empty((await StoredAsync(store, good.Id)).Faces);
    }

    private static InMemoryBlobStore Store()
    {
        var store = new InMemoryBlobStore();
        store.CreateContainer(BlobLayout.ManifestsContainer);
        return store;
    }

    private static IgnoredFacesStrip Strip(CatalogDbContext db, InMemoryBlobStore store) =>
        new(db, new ManifestStore(new ManifestValidator(), store), NullLogger.Instance);

    private static async Task<Manifest> PutAsync(InMemoryBlobStore store, int n, params (double[] Box, int Seed)[] faces)
    {
        var manifest = FaceImportTests.WithFaces(n, [.. faces.Select(f => FaceImportTests.Face(f.Box, SyntheticFaces.Unit(seed: f.Seed)))]);
        await store.WriteAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(manifest.Id), BinaryData.FromString(ManifestJson.Serialize(manifest)), "application/json", BlobWriteCondition.Always);
        return manifest;
    }

    private static async Task<Manifest> StoredAsync(InMemoryBlobStore store, string id) =>
        ManifestJson.Deserialize((await store.ReadAsync(BlobLayout.ManifestsContainer, BlobLayout.ManifestBlobName(id)))!.Content.ToString());

    // What deleting a person's face data leaves in the catalog: the box, never the embedding.
    private static async Task ForgetAsync(CatalogDbContext db, string assetId, double[] box)
    {
        db.ErasedFaces.Add(new ErasedFace { AssetId = assetId, Box = box, ErasedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }
}
