using Sepiabox.Worker.Events;

namespace Sepiabox.Worker.Tests;

/// <summary>A queue that hands out its messages in order and records how each was settled.</summary>
internal sealed class InMemoryBlobEventQueue(params BlobCreated?[] events) : IBlobEventQueue
{
    private readonly Queue<BlobEventMessage> _waiting =
        new(events.Select((e, i) => new BlobEventMessage(e, "message-" + i, 1)));

    public List<string> Completed { get; } = [];

    public List<string> Abandoned { get; } = [];

    public Task<BlobEventMessage?> ReceiveAsync(TimeSpan maxWait, CancellationToken cancellationToken) =>
        Task.FromResult(_waiting.TryDequeue(out var message) ? message : null);

    public Task CompleteAsync(BlobEventMessage message, CancellationToken cancellationToken)
    {
        Completed.Add(message.Id);
        return Task.CompletedTask;
    }

    public Task AbandonAsync(BlobEventMessage message, CancellationToken cancellationToken)
    {
        Abandoned.Add(message.Id);
        return Task.CompletedTask;
    }
}
