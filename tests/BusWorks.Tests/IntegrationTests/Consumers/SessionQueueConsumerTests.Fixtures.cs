using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Consumer;
using BusWorks.Abstractions.Events;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;

namespace BusWorks.Tests.IntegrationTests.Consumers;

public sealed partial class SessionQueueConsumerTests
{
    private const string SessionQueueName = "parking-session-queue";

    /// <summary>
    /// Domain event that requires session ordering per customer.
    /// All payments for the same <see cref="CustomerId"/> share a session and are
    /// processed FIFO. Different customers are processed concurrently.
    /// </summary>
    [QueueRoute(SessionQueueName)]
    public sealed record CustomerPaymentCreatedEvent(
        Guid Id,
        DateTime OccurredOnUtc,
        string CustomerId,
        decimal Amount) : IntegrationEvent(Id, OccurredOnUtc), ISessionedEvent
    {
        /// <summary>Stable domain key — groups all payments for one customer into the same session.</summary>
        public string SessionId => CustomerId;
    }

    /// <summary>
    /// Session-aware consumer that writes processed events into <see cref="TestConsumerCapture{T}"/>.
    /// <c>RequireSession = true</c> instructs the background service to use a
    /// <see cref="Azure.Messaging.ServiceBus.ServiceBusSessionProcessor"/> for this queue.
    /// </summary>
    [ServiceBusQueue(RequireSession = true, MaxDeliveryCount = 3)]
    internal sealed class CapturingSessionCustomerConsumer(
        TestConsumerCapture<CustomerPaymentCreatedEvent> capture)
        : IConsumer<CustomerPaymentCreatedEvent>
    {
        public Task Consume(IConsumeContext<CustomerPaymentCreatedEvent> context)
            => capture.WriteAsync(context.Message, context.Metadata, context.CancellationToken).AsTask();
    }
}


