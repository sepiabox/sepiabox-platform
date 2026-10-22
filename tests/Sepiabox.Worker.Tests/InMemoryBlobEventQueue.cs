using Sepiabox.Storage;

namespace Sepiabox.Worker.Tests;

/// <summary>A queue that hands out its messages in order and records how each was settled.</summary>
internal sealed class InMemoryBlobEventQueue : IBlobEventQueue
{
    private readonly Queue<BlobEventMessage> _waiting;

    public InMemoryBlobEventQueue(params BlobCreated?[] events)
        : this(events.Select((e, i) => new BlobEventMessage(e, "message-" + i, 1)))
    {
    }

    public InMemoryBlobEventQueue(IEnumerable<BlobEventMessage> messages) => _waiting = new(messages);

    public List<string> Completed { get; } = [];

    public List<string> Abandoned { get; } = [];

    public List<(string Id, string Reason)> DeadLettered { get; } = [];

    public List<string> Renewed { get; } = [];

    /// <summary>Messages whose lock has expired by the time they are completed.</summary>
    public HashSet<string> LockLostOnComplete { get; } = [];

    public Task<BlobEventMessage?> ReceiveAsync(TimeSpan maxWait, CancellationToken cancellationToken) =>
        Task.FromResult(_waiting.TryDequeue(out var message) ? message : null);

    public Task CompleteAsync(BlobEventMessage message, CancellationToken cancellationToken)
    {
        if (LockLostOnComplete.Contains(message.Id))
        {
            throw new BlobEventLockLostException();
        }

        Completed.Add(message.Id);
        return Task.CompletedTask;
    }

    public Task AbandonAsync(BlobEventMessage message, CancellationToken cancellationToken)
    {
        Abandoned.Add(message.Id);
        return Task.CompletedTask;
    }

    public Task RenewLockAsync(BlobEventMessage message, CancellationToken cancellationToken)
    {
        lock (Renewed)
        {
            Renewed.Add(message.Id);
        }

        return Task.CompletedTask;
    }

    public Task DeadLetterAsync(BlobEventMessage message, string reason, CancellationToken cancellationToken)
    {
        DeadLettered.Add((message.Id, reason));
        return Task.CompletedTask;
    }
}
