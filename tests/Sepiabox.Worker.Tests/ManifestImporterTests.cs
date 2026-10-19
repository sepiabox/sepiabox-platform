using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Media;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Worker.Importing;

namespace Sepiabox.Worker.Tests;

[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class ManifestImporterTests(CatalogFixture fixture)
{
    private const string PhotoId = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9";

    [Fact]
    public async Task Migrate_applies_every_migration_to_an_empty_database()
    {
        await using var db = await fixture.CreateEmptyDatabaseAsync();

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Import_once_writes_the_asset_its_sources_and_derivatives()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, Photo(Source("HD da Ana", "2019/praia.jpg")));

        var result = await Importer(db).ImportOnceAsync(container, CancellationToken.None);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Problems);
        var asset = await db.Assets.AsNoTracking().Include(a => a.Sources).Include(a => a.Derivatives).SingleAsync();
        Assert.Equal(PhotoId, asset.Id);
        Assert.Equal("photo", asset.Kind);
        Assert.Equal(new DateTime(2019, 7, 14, 16, 30, 0), asset.CapturedAt);
        Assert.Equal(DateTimeKind.Unspecified, asset.CapturedAt!.Value.Kind);
        Assert.Equal(-8.6291, asset.Location!.X, 4);
        Assert.Equal(41.1579, asset.Location.Y, 4);
        Assert.Equal(4326, asset.Location.SRID);
        Assert.Equal(("Apple", "iPhone 8"), (asset.CameraMake, asset.CameraModel));
        Assert.Equal("2019/praia.jpg", Assert.Single(asset.Sources).Path);
        Assert.Equal(
            [$"{PhotoId}/preview-1280.webp", $"{PhotoId}/thumb-256.webp"],
            asset.Derivatives.Select(d => d.BlobPath).Order());
        Assert.Equal(BlobLayout.ManifestBlobName(PhotoId), (await db.ManifestImports.SingleAsync()).BlobName);
    }

    [Fact]
    public async Task Importing_the_same_manifest_again_creates_no_duplicates()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var manifest = Photo(Source("HD da Ana", "2019/praia.jpg"));
        await PutAsync(container, manifest);
        await Importer(db).ImportOnceAsync(container, CancellationToken.None);

        var unchanged = await Importer(db).ImportOnceAsync(container, CancellationToken.None);
        await PutAsync(container, manifest); // Same content, new ETag: imported again.
        var reimported = await Importer(db).ImportOnceAsync(container, CancellationToken.None);

        Assert.Equal((0, 1), (unchanged.Imported, unchanged.Unchanged));
        Assert.Equal(1, reimported.Imported);
        Assert.Equal(1, await db.Assets.CountAsync());
        Assert.Equal(1, await db.AssetSources.CountAsync());
        Assert.Equal(2, await db.Derivatives.CountAsync());
        Assert.Equal(1, await db.ManifestImports.CountAsync());
    }

    [Fact]
    public async Task A_manifest_from_another_source_adds_a_source_to_the_existing_asset()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, Photo(Source("HD da Ana", "2019/praia.jpg")));
        await Importer(db).ImportOnceAsync(container, CancellationToken.None);

        // Another drive's work folder only knows its own copy and overwrites the same blob.
        await PutAsync(container, Photo(Source("HD do Jonathas", "Fotos/IMG_0001.jpg")));
        await Importer(db).ImportOnceAsync(container, CancellationToken.None);

        var sources = await db.AssetSources.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
        Assert.Equal(["HD da Ana", "HD do Jonathas"], sources.Select(s => s.Name));
        Assert.All(sources, s => Assert.Equal(PhotoId, s.AssetId));
        Assert.Equal(1, await db.Assets.CountAsync());
    }

    [Fact]
    public async Task Invalid_manifests_are_reported_and_valid_ones_imported()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, Photo(Source("HD da Ana", "2019/praia.jpg")));
        await container.UploadBlobAsync("broken.json", BinaryData.FromString("{ not json"));
        await container.UploadBlobAsync(
            "short-id.json",
            BinaryData.FromString(ManifestJson.Serialize(Photo(Source("HD da Ana", "x.jpg")) with { Id = "abc" })));

        var result = await Importer(db).ImportOnceAsync(container, CancellationToken.None);

        Assert.Equal(1, result.Imported);
        Assert.Equal(["broken.json", "short-id.json"], result.Problems.Select(p => p.BlobName).Order());
        Assert.Equal(1, await db.Assets.CountAsync());
        Assert.Equal(1, await db.ManifestImports.CountAsync());
    }

    private static ManifestImporter Importer(CatalogDbContext db) =>
        new(db, new ManifestValidator(), TimeProvider.System, NullLogger<ManifestImporter>.Instance);

    private static ManifestSource Source(string name, string path) =>
        new("local", name, path, new DateTimeOffset(2019, 7, 14, 17, 0, 0, TimeSpan.FromHours(1)));

    private static Manifest Photo(ManifestSource source) => new()
    {
        Id = PhotoId,
        Kind = MediaKind.Photo,
        SizeBytes = 2_345_678,
        ContentType = "image/jpeg",
        CapturedAt = new DateTime(2019, 7, 14, 16, 30, 0),
        Location = new GeoPoint(41.1579, -8.6291),
        Camera = new CameraInfo("Apple", "iPhone 8"),
        Width = 4032,
        Height = 3024,
        Sources = [source],
        Derivatives =
        [
            new ManifestDerivative("thumb-256", $"derivatives/{PhotoId}/thumb-256.webp", 256, 192, "image/webp"),
            new ManifestDerivative("preview-1280", $"derivatives/{PhotoId}/preview-1280.webp", 1280, 960, "image/webp"),
        ],
    };

    private static async Task PutAsync(BlobContainerClient container, Manifest manifest) =>
        await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id))
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(manifest)), overwrite: true);
}
