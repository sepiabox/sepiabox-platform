using System.Text.Json.Nodes;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
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
        await db.Database.ExecuteSqlRawAsync(RefuseTestPhotoSql);

        var result = await ImportAsync(db, container);

        Assert.Equal(1, result.Imported);
        Assert.Equal(BlobLayout.ManifestBlobName(PhotoId), Assert.Single(result.Problems).BlobName);
        Assert.Equal(other.Id, (await db.Assets.SingleAsync()).Id);
        Assert.Equal(BlobLayout.ManifestBlobName(other.Id), (await db.ManifestImports.SingleAsync()).BlobName);
    }

    [Fact]
    public async Task A_constraint_violation_is_reported_as_a_data_problem()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, Photo(Source("HD da Ana", "a.jpg")));
        await db.Database.ExecuteSqlRawAsync(RejectTestPhotoByCheckSql);

        var result = await ImportAsync(db, container);

        Assert.Contains("refuse_test_photo", Assert.Single(result.Problems).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_table_stops_the_import()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await PutAsync(container, Photo(Source("HD da Ana", "a.jpg")));
        await db.Database.ExecuteSqlRawAsync("DROP TABLE derivatives");

        var failure = await Record.ExceptionAsync(() => ImportAsync(db, container));

        Assert.Contains(Chain(failure), ex => ex is PostgresException { SqlState: PostgresErrorCodes.UndefinedTable });
    }

    [Fact]
    public async Task A_lost_database_connection_stops_the_import_instead_of_failing_every_manifest()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var other = Photo(Source("HD da Ana", "b.jpg")) with { Id = new string('b', 64) };
        await PutAsync(container, Photo(Source("HD da Ana", "a.jpg")));
        await PutAsync(container, other);
        await db.Database.ExecuteSqlRawAsync(DropConnectionOnTestPhotoSql);

        var failure = await Record.ExceptionAsync(() => ImportAsync(db, container));

        Assert.Contains(Chain(failure), ex => ex is PostgresException { SqlState: PostgresErrorCodes.AdminShutdown });
    }

    // EF's execution strategy wraps database errors; what matters is that they escaped instead of becoming a problem.
    private static List<Exception> Chain(Exception? failure)
    {
        var chain = new List<Exception>();
        for (var current = failure; current is not null; current = current.InnerException)
        {
            chain.Add(current);
        }

        return chain;
    }

    // These fire only for the test photo, so the other manifests in a test import normally.
    private const string RefuseTestPhotoSql = $$"""
        CREATE FUNCTION refuse_asset() RETURNS trigger LANGUAGE plpgsql AS
        $$ BEGIN RAISE EXCEPTION 'refused by test'; END $$;
        CREATE TRIGGER refuse BEFORE INSERT ON assets FOR EACH ROW
        WHEN (NEW.id = '{{PhotoId}}') EXECUTE FUNCTION refuse_asset();
        """;

    // SQLSTATE 23514, a check violation: a data class.
    private const string RejectTestPhotoByCheckSql = $$"""
        ALTER TABLE assets ADD CONSTRAINT refuse_test_photo CHECK (id <> '{{PhotoId}}');
        """;

    // The server ends this session, as when the database restarts mid-import (SQLSTATE 57P01).
    private const string DropConnectionOnTestPhotoSql = $$"""
        CREATE FUNCTION drop_connection() RETURNS trigger LANGUAGE plpgsql AS
        $$ BEGIN PERFORM pg_terminate_backend(pg_backend_pid()); RETURN NEW; END $$;
        CREATE TRIGGER drop_connection BEFORE INSERT ON assets FOR EACH ROW
        WHEN (NEW.id = '{{PhotoId}}') EXECUTE FUNCTION drop_connection();
        """;

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
