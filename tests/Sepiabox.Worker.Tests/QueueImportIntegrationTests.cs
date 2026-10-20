using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Worker.Events;
using Sepiabox.Worker.Importing;
using static Sepiabox.Tests.Shared.TestManifests;

namespace Sepiabox.Worker.Tests;

/// <summary><c>import --from-queue</c> on PostgreSQL and Azurite, with an in-memory queue.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class QueueImportIntegrationTests(CatalogFixture fixture)
{
    private static readonly string s_manifest = BlobLayout.ManifestBlobName(PhotoId);

    [Fact]
    public async Task Manifest_named_by_an_event_is_imported_and_its_message_completed()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await container.GetBlobClient(s_manifest)
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(Photo(Source("HD da Ana", "2019/praia.jpg")))));
        var queue = new InMemoryBlobEventQueue(
            new BlobCreated(BlobLayout.ManifestsContainer, s_manifest),
            // The same manifest again (an overwrite, or a duplicate delivery): imported again, changing nothing.
            new BlobCreated(BlobLayout.ManifestsContainer, s_manifest));

        var result = await Import(queue, db, container).RunAsync(CancellationToken.None);

        Assert.Equal(2, result.Imported);
        Assert.Empty(result.Problems);
        Assert.Equal(["message-0", "message-1"], queue.Completed);
        Assert.Equal(PhotoId, (await db.Assets.AsNoTracking().SingleAsync()).Id);
        Assert.Equal(s_manifest, (await db.ManifestImports.AsNoTracking().SingleAsync()).BlobName);
    }

    [Fact]
    public async Task An_invalid_manifest_is_reported_and_its_message_completed_so_it_is_not_retried()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await container.GetBlobClient(s_manifest).UploadAsync(BinaryData.FromString("{}"));
        var queue = new InMemoryBlobEventQueue(new BlobCreated(BlobLayout.ManifestsContainer, s_manifest));

        var result = await Import(queue, db, container).RunAsync(CancellationToken.None);

        Assert.Equal(s_manifest, Assert.Single(result.Problems).BlobName);
        Assert.Equal(["message-0"], queue.Completed);
        Assert.Empty(await db.Assets.ToListAsync());
    }

    [Fact]
    public async Task A_manifest_deleted_since_the_event_is_skipped()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        var queue = new InMemoryBlobEventQueue(new BlobCreated(BlobLayout.ManifestsContainer, s_manifest));

        var result = await Import(queue, db, container).RunAsync(CancellationToken.None);

        Assert.Equal(0, result.Imported);
        Assert.Empty(result.Problems);
        Assert.Equal(["message-0"], queue.Completed);
    }

    [Fact]
    public async Task A_storage_failure_leaves_the_message_to_its_lock_and_fails_the_run()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        // Nothing listens there: the download fails, which says nothing about the manifest.
        var unreachable = new BlobContainerClient(
            new Uri("http://127.0.0.1:1/devstoreaccount1/" + BlobLayout.ManifestsContainer),
            new BlobClientOptions { Retry = { MaxRetries = 0, NetworkTimeout = TimeSpan.FromSeconds(2) } });
        var queue = new InMemoryBlobEventQueue(new BlobCreated(BlobLayout.ManifestsContainer, s_manifest));

        await Assert.ThrowsAnyAsync<Exception>(() => Import(queue, db, unreachable).RunAsync(CancellationToken.None));

        // It comes back when its lock expires, not at once (D-063).
        Assert.Empty(queue.Abandoned);
        Assert.Empty(queue.Completed);
    }

    [Fact]
    public async Task A_database_failure_leaves_the_message_to_its_lock_and_fails_the_run()
    {
        // An empty database without the catalog's tables: saving fails for every manifest alike.
        await using var db = await fixture.CreateEmptyDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await container.GetBlobClient(s_manifest)
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(Photo(Source("HD da Ana", "2019/praia.jpg")))));
        var queue = new InMemoryBlobEventQueue(new BlobCreated(BlobLayout.ManifestsContainer, s_manifest));

        await Assert.ThrowsAnyAsync<Exception>(() => Import(queue, db, container).RunAsync(CancellationToken.None));

        // It comes back when its lock expires, not at once (D-063).
        Assert.Empty(queue.Abandoned);
        Assert.Empty(queue.Completed);
    }

    [Fact]
    public async Task A_race_with_another_import_comes_back_instead_of_being_reported()
    {
        await using var db = await fixture.CreateMigratedDatabaseAsync();
        var container = await fixture.CreateContainerAsync();
        await container.GetBlobClient(s_manifest)
            .UploadAsync(BinaryData.FromString(ManifestJson.Serialize(Photo(Source("HD da Ana", "2019/praia.jpg")))));
        // As when import-all inserts the same new asset at the same moment (SQLSTATE 23505).
        await db.Database.ExecuteSqlRawAsync($$"""
            CREATE FUNCTION duplicate_asset() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN RAISE EXCEPTION 'duplicate by test' USING ERRCODE = 'unique_violation'; END $$;
            CREATE TRIGGER duplicate BEFORE INSERT ON assets FOR EACH ROW EXECUTE FUNCTION duplicate_asset();
            """);
        var queue = new InMemoryBlobEventQueue(new BlobCreated(BlobLayout.ManifestsContainer, s_manifest));

        await Assert.ThrowsAsync<DbUpdateException>(() => Import(queue, db, container).RunAsync(CancellationToken.None));

        Assert.Empty(queue.Completed);
        // import --once reports the same failure as a problem of the manifest and goes on.
        var once = await Importer(db).ImportOnceAsync(container, CancellationToken.None);
        Assert.Equal(s_manifest, Assert.Single(once.Problems).BlobName);
    }

    private static ManifestImporter Importer(CatalogDbContext db) =>
        new(db, new ManifestValidator(), new PostgisPlaceResolver(db), TimeProvider.System, NullLogger<ManifestImporter>.Instance);

    private static QueueImport Import(IBlobEventQueue queue, CatalogDbContext db, BlobContainerClient container) =>
        new(
            queue,
            (blobName, token) => Importer(db).ImportOneAsync(container, blobName, token),
            QueueImportOptions.Default with { IdleWait = TimeSpan.Zero },
            NullLogger<QueueImport>.Instance);
}
