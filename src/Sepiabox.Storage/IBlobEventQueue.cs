using System.Diagnostics.CodeAnalysis;

namespace Sepiabox.Storage;

/// <summary>A blob was created (or overwritten) in a container: what the worker needs from a storage event.</summary>
public sealed record BlobCreated(string Container, string BlobName);

/// <summary>
/// One message taken from the queue. <see cref="Event"/> is null when the message is not a blob-created
/// event the worker understands; it is completed like any other, so it never comes back.
/// </summary>
public sealed record BlobEventMessage(BlobCreated? Event, string Id, int DeliveryCount);

/// <summary>
/// Where the worker receives "blob created" events from (S-033, D-051): Event Grid and Service Bus in Azure,
/// an in-memory queue in tests. A message received is locked until it is completed or abandoned.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "It is a queue; the name is the one D-051 gives it.")]
public interface IBlobEventQueue
{
    /// <summary>The next message, or null when none arrives within <paramref name="maxWait"/>: the queue is empty.</summary>
    Task<BlobEventMessage?> ReceiveAsync(TimeSpan maxWait, CancellationToken cancellationToken);

    /// <summary>Removes the message: it was handled, or can never be.</summary>
    Task CompleteAsync(BlobEventMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Puts the message back to be received again at once; after the queue's maximum delivery count it is
    /// dead-lettered instead.
    /// </summary>
    Task AbandonAsync(BlobEventMessage message, CancellationToken cancellationToken);

    /// <summary>Keeps the message locked to this receiver for another lock period.</summary>
    Task RenewLockAsync(BlobEventMessage message, CancellationToken cancellationToken);

    /// <summary>Moves the message to the dead-letter queue, with the reason it could not be handled.</summary>
    Task DeadLetterAsync(BlobEventMessage message, string reason, CancellationToken cancellationToken);
}

/// <summary>
/// The message's lock expired before it was settled: the queue delivers it again (or already has), so it can
/// no longer be settled here.
/// </summary>
public sealed class BlobEventLockLostException(string message, Exception? innerException)
    : Exception(message, innerException)
{
    public BlobEventLockLostException()
        : this("The message's lock was lost.", null)
    {
    }

    public BlobEventLockLostException(string message)
        : this(message, null)
    {
    }
}
