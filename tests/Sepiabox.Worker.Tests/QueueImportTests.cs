using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Catalog;
using Sepiabox.Pipeline.Manifests;
using Sepiabox.Pipeline.Storage;
using Sepiabox.Worker.Events;
using Sepiabox.Worker.Importing;

namespace Sepiabox.Worker.Tests;

/// <summary><c>import --from-queue</c> without a database or storage: the messages it never imports.</summary>
public sealed class QueueImportTests
{
    [Fact]
    public async Task Events_from_other_containers_and_other_messages_are_ignored_and_completed()
    {
        var queue = new InMemoryBlobEventQueue(
            new BlobCreated("originals", "0a1b.jpg"),
            null,
            new BlobCreated("derivatives", "0a1b/thumb-256.webp"));
        await using var db = UnusedCatalog();

        var result = await Import(queue, db).RunAsync(CancellationToken.None);

        Assert.Equal(["message-0", "message-1", "message-2"], queue.Completed);
        Assert.Empty(queue.Abandoned);
        Assert.Equal(0, result.Imported);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public async Task An_empty_queue_ends_the_run_at_once()
    {
        var queue = new InMemoryBlobEventQueue();
        await using var db = UnusedCatalog();

        var result = await Import(queue, db).RunAsync(CancellationToken.None);

        Assert.Equal(0, result.Imported);
        Assert.Empty(queue.Completed);
    }

    private static QueueImport Import(IBlobEventQueue queue, CatalogDbContext db) =>
        new(
            queue,
            new ManifestImporter(db, new ManifestValidator(), TimeProvider.System, NullLogger<ManifestImporter>.Instance),
            // Never reached: no event here is about the manifests container.
            new BlobContainerClient(new Uri("https://example.invalid/" + BlobLayout.ManifestsContainer), new BlobClientOptions { Retry = { MaxRetries = 0 } }),
            NullLogger<QueueImport>.Instance);

    // Never opened: an ignored event touches neither the database nor storage.
    private static CatalogDbContext UnusedCatalog()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContext.Configure(options, "Host=127.0.0.1;Port=1;Database=unused;Timeout=1");
        return new CatalogDbContext(options.Options);
    }
}
