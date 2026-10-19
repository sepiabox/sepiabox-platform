using System.Text.Json.Nodes;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Worker.Importing;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Worker.Tests;

[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class ManifestImporterTests(CatalogFixture fixture)
{
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Migrate_applies_every_migration_to_an_empty_database()
    {
        await using var db = await fixture.CreateEmptyDatabaseAsync();

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges(), "The model has changes no migration describes.");
    }

    [Fact]
    public async Task Import_once_writes_the_asset_its_sources_and_derivatives()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, Photo(Source("HD da Ana", "2019/praia.jpg")));

        var result = await ImportAsync(db, container);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Problems);
        var asset = await LoadAsync(db);
        Assert.Equal(PhotoId, asset.Id);
        Assert.Equal("photo", asset.Kind);
        Assert.Equal(new DateTime(2019, 7, 14, 16, 30, 0), asset.CapturedAt);
        Assert.Equal(DateTimeKind.Unspecified, asset.CapturedAt!.Value.Kind);
        Assert.Equal(-8.6291, asset.Location!.X, 4);
        Assert.Equal(41.1579, asset.Location.Y, 4);
        Assert.Equal(4326, asset.Location.SRID);
        Assert.Equal(("Apple", "iPhone 8"), (asset.CameraMake, asset.CameraModel));
        Assert.Equal((_clock.Now, _clock.Now), (asset.ImportedAt, asset.UpdatedAt));
        Assert.Equal("2019/praia.jpg", Assert.Single(asset.Sources).Path);
        Assert.Equal(
            [$"{PhotoId}/preview-1280.webp", $"{PhotoId}/thumb-256.webp"],
            asset.Derivatives.Select(d => d.BlobPath).Order(StringComparer.Ordinal));
        Assert.Equal(BlobLayout.ManifestBlobName(PhotoId), (await db.ManifestImports.SingleAsync()).BlobName);
    }

    [Fact]
    public async Task Importing_the_same_manifest_again_creates_no_duplicates()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var manifest = Photo(Source("HD da Ana", "2019/praia.jpg"));
        await PutAsync(container, manifest);
        await ImportAsync(db, container);

        var unchanged = await ImportAsync(db, container);
        await PutAsync(container, manifest); // Same content, new ETag: imported again.
        var reimported = await ImportAsync(db, container);

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
        await ImportAsync(db, container);

        await PutAsync(container, Photo(Source("HD do Jonathas", "Fotos/IMG_0001.jpg")));
        await ImportAsync(db, container);

        var sources = await db.AssetSources.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
        Assert.Equal(["HD da Ana", "HD do Jonathas"], sources.Select(s => s.Name));
        Assert.All(sources, s => Assert.Equal(PhotoId, s.AssetId));
        Assert.Equal(1, await db.Assets.CountAsync());
    }

    [Fact]
    public async Task The_same_file_with_windows_and_linux_separators_is_one_source()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, Photo(Source("HD da Ana", "2019\\praia.jpg")));
        await ImportAsync(db, container);

        await PutAsync(container, Photo(Source("HD da Ana", "2019/praia.jpg", hour: 18)));
        await ImportAsync(db, container);

        Assert.Equal("2019/praia.jpg", (await db.AssetSources.SingleAsync()).Path);
    }

    [Fact]
    public async Task A_changed_manifest_updates_the_asset_its_derivatives_and_source_times()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, Photo(Source("HD da Ana", "2019/praia.jpg", hour: 17)));
        await ImportAsync(db, container);
        var firstImport = _clock.Now;

        _clock.Now = firstImport.AddDays(1);
        await PutAsync(container, Photo(Source("HD da Ana", "2019/praia.jpg", hour: 20)) with
        {
            Width = 3024,
            CapturedAt = new DateTime(2019, 7, 15, 9, 0, 0),
            Derivatives = [Derivative("thumb-256", 192, 256), Derivative("square-512", 512, 512)],
        });
        await ImportAsync(db, container);

        var asset = await LoadAsync(db);
        Assert.Equal(3024, asset.Width);
        Assert.Equal(new DateTime(2019, 7, 15, 9, 0, 0), asset.CapturedAt);
        Assert.Equal((firstImport, _clock.Now), (asset.ImportedAt, asset.UpdatedAt));
        Assert.Equal(["square-512:512", "thumb-256:192"], asset.Derivatives.Select(d => $"{d.Name}:{d.Width}").Order(StringComparer.Ordinal));
        Assert.Equal(new DateTimeOffset(2019, 7, 14, 19, 0, 0, TimeSpan.Zero), Assert.Single(asset.Sources).ModifiedAt);
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

        var result = await ImportAsync(db, container);

        Assert.Equal(1, result.Imported);
        Assert.Equal(["broken.json", "short-id.json"], result.Problems.Select(p => p.BlobName).Order(StringComparer.Ordinal));
        Assert.Equal(1, await db.Assets.CountAsync());
        Assert.Equal(1, await db.ManifestImports.CountAsync());
    }

    [Fact]
    public async Task A_capture_time_with_an_offset_is_reported_without_stopping_the_import()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var json = JsonNode.Parse(ManifestJson.Serialize(Photo(Source("HD da Ana", "a.jpg"))))!;
        json["capturedAt"] = "2019-07-14T16:30:00Z";
        await container.UploadBlobAsync(BlobLayout.ManifestBlobName(PhotoId), BinaryData.FromString(json.ToJsonString()));

        var result = await ImportAsync(db, container);

        Assert.Contains("/capturedAt", Assert.Single(result.Problems).Reason, StringComparison.Ordinal);
        Assert.Equal(0, await db.Assets.CountAsync());
    }

    [Fact]
    public async Task A_manifest_stored_under_another_asset_name_is_reported()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await container.UploadBlobAsync(
            "f00d.json", BinaryData.FromString(ManifestJson.Serialize(Photo(Source("HD da Ana", "a.jpg")))));

        var result = await ImportAsync(db, container);

        Assert.Equal("f00d.json", Assert.Single(result.Problems).BlobName);
        Assert.Equal(0, await db.Assets.CountAsync());
    }

    [Fact]
    public async Task A_manifest_deleted_after_listing_is_skipped_and_the_rest_imported()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var other = Photo(Source("HD da Ana", "b.jpg")) with { Id = new string('b', 64) };
        await PutAsync(container, Photo(Source("HD da Ana", "a.jpg")));
        await PutAsync(container, other);
        var vanishing = new VanishingBlobContainer(fixture.BlobConnectionString, container.Name, BlobLayout.ManifestBlobName(PhotoId));

        var result = await ImportAsync(db, vanishing);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Problems);
        Assert.Equal(other.Id, (await db.Assets.SingleAsync()).Id);
    }

    [Fact]
    public async Task A_database_error_on_one_manifest_is_reported_and_the_rest_imported()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var other = Photo(Source("HD da Ana", "b.jpg")) with { Id = new string('b', 64) };
        await PutAsync(container, Photo(Source("HD da Ana", "a.jpg")));
        await PutAsync(container, other);
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE FUNCTION refuse_asset() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN RAISE EXCEPTION 'refused by test'; END $$;
            CREATE TRIGGER refuse BEFORE INSERT ON assets FOR EACH ROW
            WHEN (NEW.id = '{PhotoId}') EXECUTE FUNCTION refuse_asset();
            """);

        var result = await ImportAsync(db, container);

        Assert.Equal(1, result.Imported);
        Assert.Equal(BlobLayout.ManifestBlobName(PhotoId), Assert.Single(result.Problems).BlobName);
        Assert.Equal(other.Id, (await db.Assets.SingleAsync()).Id);
        Assert.Equal(BlobLayout.ManifestBlobName(other.Id), (await db.ManifestImports.SingleAsync()).BlobName);
    }

    private Task<ImportResult> ImportAsync(CatalogDbContext db, BlobContainerClient container) =>
        new ManifestImporter(db, new ManifestValidator(), _clock, NullLogger<ManifestImporter>.Instance)
            .ImportOnceAsync(container, CancellationToken.None);

    private static Task<Asset> LoadAsync(CatalogDbContext db) =>
        db.Assets.AsNoTracking().Include(a => a.Sources).Include(a => a.Derivatives).SingleAsync();

    private static async Task PutAsync(BlobContainerClient container, Manifest manifest) =>
        await container.GetBlobClient(BlobLayout.ManifestBlobName(manifest.Id))
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(manifest)), overwrite: true);

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Lists every blob, but one of them is gone by the time it is downloaded.</summary>
    private sealed class VanishingBlobContainer(string connectionString, string name, string vanishing)
        : BlobContainerClient(connectionString, name)
    {
        public override BlobClient GetBlobClient(string blobName) =>
            base.GetBlobClient(blobName == vanishing ? blobName + ".deleted" : blobName);
    }
}
