using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Storage;
using Sepiabox.Worker.Events;
using Sepiabox.Worker.Importing;

namespace Sepiabox.Worker.Tests;

/// <summary><c>import --from-queue</c> with an in-memory queue and a stand-in for the import (S-033, D-063).</summary>
public sealed class QueueImportTests
{
    private static readonly QueueImportOptions s_options = new(5, TimeSpan.FromMilliseconds(20), TimeSpan.Zero);

    private readonly List<string> _imported = [];

    [Fact]
    public async Task Events_from_other_containers_and_other_messages_are_ignored_and_completed()
    {
        var queue = new InMemoryBlobEventQueue(
            new BlobCreated("originals", "0a1b.jpg"),
            null,
            new BlobCreated("derivatives", "0a1b/thumb-256.webp"));

        var result = await Import(queue).RunAsync(CancellationToken.None);

        Assert.Equal(["message-0", "message-1", "message-2"], queue.Completed);
        Assert.Empty(_imported);
        Assert.Equal(0, result.Imported);
    }

    [Fact]
    public async Task An_empty_queue_ends_the_run_at_once()
    {
        var queue = new InMemoryBlobEventQueue();

        var result = await Import(queue).RunAsync(CancellationToken.None);

        Assert.Equal(0, result.Imported);
        Assert.Empty(queue.Completed);
    }

    [Fact]
    public async Task Each_manifest_event_is_imported_and_completed_and_problems_are_reported()
    {
        var queue = new InMemoryBlobEventQueue(Manifest("a.json"), Manifest("bad.json"));

        var result = await Import(queue, (name, _) => Task.FromResult(name == "bad.json"
            ? new ImportResult(0, 0, [new ImportProblem(name, "invalid")], [])
            : new ImportResult(1, 0, [], []))).RunAsync(CancellationToken.None);

        Assert.Equal(["message-0", "message-1"], queue.Completed);
        Assert.Equal(1, result.Imported);
        Assert.Equal("bad.json", Assert.Single(result.Problems).BlobName);
    }

    [Fact]
    public async Task The_lock_is_renewed_while_a_long_import_runs()
    {
        var queue = new InMemoryBlobEventQueue(Manifest("slow.json"));

        await Import(queue, async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), token);
            return new ImportResult(1, 0, [], []);
        }).RunAsync(CancellationToken.None);

        Assert.True(queue.Renewed.Count >= 3, $"Renewed {queue.Renewed.Count} times.");
        Assert.Equal(["message-0"], queue.Completed);
    }

    [Fact]
    public async Task A_lock_lost_before_completing_is_logged_and_the_run_goes_on()
    {
        var queue = new InMemoryBlobEventQueue(Manifest("a.json"), Manifest("b.json"));
        queue.LockLostOnComplete.Add("message-0");

        var result = await Import(queue).RunAsync(CancellationToken.None);

        Assert.Equal(["message-1"], queue.Completed);
        Assert.Equal(2, result.Imported);
    }

    [Fact]
    public async Task An_infrastructure_failure_leaves_the_message_to_its_lock_and_fails_the_run()
    {
        var queue = new InMemoryBlobEventQueue(
            [new BlobEventMessage(Manifest("a.json"), "message-0", 2), new BlobEventMessage(Manifest("b.json"), "message-1", 1)]);

        await Assert.ThrowsAsync<TimeoutException>(
            () => Import(queue, (_, _) => throw new TimeoutException("database restarting")).RunAsync(CancellationToken.None));

        // Neither given back at once nor completed: it returns when its lock expires, minutes later.
        Assert.Empty(queue.Abandoned);
        Assert.Empty(queue.Completed);
        Assert.Empty(queue.DeadLettered);
    }

    [Fact]
    public async Task A_failure_on_the_last_delivery_dead_letters_the_message_with_the_reason()
    {
        var queue = new InMemoryBlobEventQueue([new BlobEventMessage(Manifest("a.json"), "message-0", 5)]);

        await Assert.ThrowsAsync<TimeoutException>(
            () => Import(queue, (_, _) => throw new TimeoutException("database restarting")).RunAsync(CancellationToken.None));

        var (id, reason) = Assert.Single(queue.DeadLettered);
        Assert.Equal("message-0", id);
        Assert.Equal("Import failed on delivery 5: TimeoutException", reason);
    }

    [Fact]
    public async Task Stopping_gives_the_message_back_at_once()
    {
        var queue = new InMemoryBlobEventQueue(Manifest("a.json"));
        using var stop = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Import(queue, async (_, token) =>
        {
            await stop.CancelAsync();
            token.ThrowIfCancellationRequested();
            return new ImportResult(1, 0, [], []);
        }).RunAsync(stop.Token));

        Assert.Equal(["message-0"], queue.Abandoned);
        Assert.Empty(queue.Completed);
    }

    private static BlobCreated Manifest(string name) => new(BlobLayout.ManifestsContainer, name);

    private QueueImport Import(IBlobEventQueue queue, ImportManifest? importManifest = null) =>
        new(queue, importManifest ?? Record, s_options, NullLogger<QueueImport>.Instance);

    private Task<ImportResult> Record(string blobName, CancellationToken cancellationToken)
    {
        _imported.Add(blobName);
        return Task.FromResult(new ImportResult(1, 0, [], []));
    }
}
