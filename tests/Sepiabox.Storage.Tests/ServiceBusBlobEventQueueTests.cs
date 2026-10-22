using System.Diagnostics.CodeAnalysis;
using Azure.Messaging.ServiceBus;
using Sepiabox.Storage.Azure;

namespace Sepiabox.Storage.Tests;

/// <summary>The Azure implementation's mapping, on the SDK's own test doubles (no Service Bus).</summary>
public sealed class ServiceBusBlobEventQueueTests
{
    private const string Event = """
        {"eventType": "Microsoft.Storage.BlobCreated", "subject": "/blobServices/default/containers/manifests/blobs/a.json"}
        """;

    [Fact]
    public async Task Received_message_carries_the_parsed_event_its_lock_token_and_delivery_count()
    {
        var receiver = new FakeReceiver();
        await using var queue = new ServiceBusBlobEventQueue(new FakeClient(), receiver);

        var message = await queue.ReceiveAsync(TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(new BlobCreated("manifests", "a.json"), message!.Event);
        Assert.Equal(receiver.LockToken.ToString(), message.Id);
        Assert.Equal(3, message.DeliveryCount);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("abandon")]
    [InlineData("dead-letter")]
    [InlineData("renew")]
    public async Task A_lost_lock_becomes_the_queue_s_own_exception(string operation)
    {
        var receiver = new FakeReceiver { LockLost = true };
        await using var queue = new ServiceBusBlobEventQueue(new FakeClient(), receiver);
        var message = (await queue.ReceiveAsync(TimeSpan.Zero, CancellationToken.None))!;

        var error = await Assert.ThrowsAsync<BlobEventLockLostException>(() => operation switch
        {
            "complete" => queue.CompleteAsync(message, CancellationToken.None),
            "abandon" => queue.AbandonAsync(message, CancellationToken.None),
            "dead-letter" => queue.DeadLetterAsync(message, "reason", CancellationToken.None),
            _ => queue.RenewLockAsync(message, CancellationToken.None),
        });

        Assert.Equal(ServiceBusFailureReason.MessageLockLost, Assert.IsType<ServiceBusException>(error.InnerException).Reason);
    }

    [Fact]
    public async Task Other_service_bus_failures_are_not_reported_as_a_lost_lock()
    {
        var receiver = new FakeReceiver { Failure = ServiceBusFailureReason.ServiceTimeout };
        await using var queue = new ServiceBusBlobEventQueue(new FakeClient(), receiver);
        var message = (await queue.ReceiveAsync(TimeSpan.Zero, CancellationToken.None))!;

        var error = await Assert.ThrowsAsync<ServiceBusException>(() => queue.CompleteAsync(message, CancellationToken.None));

        Assert.Equal(ServiceBusFailureReason.ServiceTimeout, error.Reason);
    }

    [Fact]
    public async Task A_message_is_settled_on_the_receiver_once()
    {
        var receiver = new FakeReceiver();
        await using var queue = new ServiceBusBlobEventQueue(new FakeClient(), receiver);
        var message = (await queue.ReceiveAsync(TimeSpan.Zero, CancellationToken.None))!;

        await queue.CompleteAsync(message, CancellationToken.None);

        Assert.Equal(1, receiver.Completed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => queue.CompleteAsync(message, CancellationToken.None));
    }

    private sealed class FakeClient : ServiceBusClient
    {
        [SuppressMessage("Usage", "CA2215:Dispose methods should call base class dispose", Justification = "The SDK's mocking constructor opens no connection, and its DisposeAsync fails without one.")]
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeReceiver : ServiceBusReceiver
    {
        public Guid LockToken { get; } = Guid.NewGuid();

        public bool LockLost { get; init; }

        public ServiceBusFailureReason? Failure { get; init; }

        public int Completed { get; private set; }

        public override Task<ServiceBusReceivedMessage?> ReceiveMessageAsync(
            TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<ServiceBusReceivedMessage?>(ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromString(Event), lockTokenGuid: LockToken, deliveryCount: 3));

        public override Task CompleteMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
        {
            Fail();
            Completed++;
            return Task.CompletedTask;
        }

        public override Task AbandonMessageAsync(
            ServiceBusReceivedMessage message, IDictionary<string, object>? propertiesToModify = null, CancellationToken cancellationToken = default)
        {
            Fail();
            return Task.CompletedTask;
        }

        public override Task DeadLetterMessageAsync(
            ServiceBusReceivedMessage message, string deadLetterReason, string? deadLetterErrorDescription = null, CancellationToken cancellationToken = default)
        {
            Fail();
            return Task.CompletedTask;
        }

        public override Task RenewMessageLockAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
        {
            Fail();
            return Task.CompletedTask;
        }

        [SuppressMessage("Usage", "CA2215:Dispose methods should call base class dispose", Justification = "The SDK's mocking constructor opens no connection, and its DisposeAsync fails without one.")]
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void Fail()
        {
            if (LockLost)
            {
                throw new ServiceBusException("The lock supplied is invalid.", ServiceBusFailureReason.MessageLockLost);
            }

            if (Failure is { } reason)
            {
                throw new ServiceBusException("Failed.", reason);
            }
        }
    }
}
