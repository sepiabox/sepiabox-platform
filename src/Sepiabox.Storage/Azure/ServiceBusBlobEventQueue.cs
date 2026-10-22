using System.Diagnostics.CodeAnalysis;
using Azure.Messaging.ServiceBus;

namespace Sepiabox.Storage.Azure;

/// <summary>
/// The Azure implementation of <see cref="IBlobEventQueue"/> (S-033, D-051): Event Grid sends the storage
/// account's BlobCreated events for the <c>manifests</c> container to the Service Bus queue <c>manifests</c>,
/// read here in peek-lock mode, one message at a time.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "It is a queue; the name is the one D-051 gives it.")]
public sealed class ServiceBusBlobEventQueue(ServiceBusClient client, ServiceBusReceiver receiver) : IBlobEventQueue, IAsyncDisposable
{
    // The message each BlobEventMessage stands for, so it can be settled; one at a time, so this stays small.
    private readonly Dictionary<string, ServiceBusReceivedMessage> _locked = new(StringComparer.Ordinal);

    public async Task<BlobEventMessage?> ReceiveAsync(TimeSpan maxWait, CancellationToken cancellationToken)
    {
        var message = await receiver.ReceiveMessageAsync(maxWait, cancellationToken);
        if (message is null)
        {
            return null;
        }

        var token = message.LockToken;
        _locked[token] = message;
        return new BlobEventMessage(EventGridBlobEvents.Parse(message.Body), token, message.DeliveryCount);
    }

    public Task CompleteAsync(BlobEventMessage message, CancellationToken cancellationToken) =>
        SettleAsync(message, received => receiver.CompleteMessageAsync(received, cancellationToken));

    public Task AbandonAsync(BlobEventMessage message, CancellationToken cancellationToken) =>
        SettleAsync(message, received => receiver.AbandonMessageAsync(received, cancellationToken: cancellationToken));

    public Task DeadLetterAsync(BlobEventMessage message, string reason, CancellationToken cancellationToken) =>
        SettleAsync(message, received => receiver.DeadLetterMessageAsync(received, reason, cancellationToken: cancellationToken));

    public async Task RenewLockAsync(BlobEventMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var received = _locked.TryGetValue(message.Id, out var found)
            ? found
            : throw new InvalidOperationException($"Message {message.Id} was not received here or was already settled.");
        await TranslateLockLostAsync(() => receiver.RenewMessageLockAsync(received, cancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        await receiver.DisposeAsync();
        await client.DisposeAsync();
    }

    private async Task SettleAsync(BlobEventMessage message, Func<ServiceBusReceivedMessage, Task> settle)
    {
        ArgumentNullException.ThrowIfNull(message);
        var received = Take(message);
        await TranslateLockLostAsync(() => settle(received));
    }

    // Keeps Service Bus's exception inside this implementation (D-051).
    private static async Task TranslateLockLostAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessageLockLost)
        {
            throw new BlobEventLockLostException(ex.Message, ex);
        }
    }

    private ServiceBusReceivedMessage Take(BlobEventMessage message) =>
        _locked.Remove(message.Id, out var received)
            ? received
            : throw new InvalidOperationException($"Message {message.Id} was not received here or was already settled.");
}
