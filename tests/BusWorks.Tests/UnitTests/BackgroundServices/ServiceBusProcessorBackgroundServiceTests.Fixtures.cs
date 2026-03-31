using Azure.Messaging.ServiceBus;
using BusWorks.Attributes;
using BusWorks.Consumer;

namespace BusWorks.Tests.UnitTests.BackgroundServices;

internal sealed partial class ServiceBusProcessorBackgroundServiceTests
{
    // Message types
    [QueueRoute("order-queue")]
    private sealed record QueueMessage(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;

    [TopicRoute("park-events")]
    private sealed record TopicMessage(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;

    private sealed record UnroutedMessage(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;

    [QueueRoute("session-queue")]
    private sealed record SessionMessage(Guid Id, DateTime OccurredOnUtc) : ISessionedEvent
    {
        public string SessionId => Id.ToString();
    }
    
    [ServiceBusQueue]
    private sealed class ImplicitQueueConsumer : ServiceBusConsumer<QueueMessage>
    {
        protected override Task ProcessMessageAsync(QueueMessage message, ServiceBusReceivedMessage originalMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [ServiceBusQueue("explicit-queue")]
    private sealed class ExplicitQueueConsumer : ServiceBusConsumer<QueueMessage>
    {
        protected override Task ProcessMessageAsync(QueueMessage message, ServiceBusReceivedMessage originalMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [ServiceBusQueue(RequireSession = true, MaxDeliveryCount = 3)]
    private sealed class SessionQueueConsumer : ServiceBusConsumer<SessionMessage>
    {
        protected override Task ProcessMessageAsync(SessionMessage message, ServiceBusReceivedMessage originalMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [ServiceBusQueue(MaxDeliveryCount = -1)]
    private sealed class NegativeDeliveryCountQueueConsumer : ServiceBusConsumer<QueueMessage>
    {
        protected override Task ProcessMessageAsync(QueueMessage message, ServiceBusReceivedMessage originalMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [ServiceBusQueue]  // Queue consumer, but message type has [TopicRoute]
    private sealed class QueueConsumerWithTopicMessage : ServiceBusConsumer<TopicMessage>
    {
        protected override Task ProcessMessageAsync(TopicMessage message, ServiceBusReceivedMessage originalMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [ServiceBusQueue]  // Queue consumer, but message type has no route attribute
    private sealed class QueueConsumerWithUnroutedMessage : ServiceBusConsumer<UnroutedMessage>
    {
        protected override Task ProcessMessageAsync(UnroutedMessage message, ServiceBusReceivedMessage originalMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // Topic consumers
    [ServiceBusTopic("resort-subscription")]
    private sealed class TopicConsumer : ServiceBusConsumer<TopicMessage>
    {
        protected override Task ProcessMessageAsync(TopicMessage message, ServiceBusReceivedMessage originalMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [ServiceBusTopic("resort-subscription", MaxDeliveryCount = -1)]
    private sealed class NegativeDeliveryCountTopicConsumer : ServiceBusConsumer<TopicMessage>
    {
        protected override Task ProcessMessageAsync(TopicMessage message, ServiceBusReceivedMessage originalMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [ServiceBusTopic("resort-subscription")]  // Topic consumer, but message type has [QueueRoute]
    private sealed class TopicConsumerWithQueueMessage : ServiceBusConsumer<QueueMessage>
    {
        protected override Task ProcessMessageAsync(QueueMessage message, ServiceBusReceivedMessage originalMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [ServiceBusTopic("resort-subscription")]  // Topic consumer, but message type has no route attribute
    private sealed class TopicConsumerWithUnroutedMessage : ServiceBusConsumer<UnroutedMessage>
    {
        protected override Task ProcessMessageAsync(UnroutedMessage message, ServiceBusReceivedMessage originalMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // Consumer with no routing attribute at all
    private sealed class UnattributedConsumer : ServiceBusConsumer<QueueMessage>
    {
        protected override Task ProcessMessageAsync(QueueMessage message, ServiceBusReceivedMessage originalMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // Raw (non-generic) consumer
    [ServiceBusQueue("raw-queue")]
    private sealed class RawQueueConsumer : ServiceBusConsumer
    {
        protected override Task ProcessMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // Session contract mismatch fixtures
    [ServiceBusQueue]  // RequireSession = false, but message implements ISessionedEvent
    private sealed class NonSessionConsumerForSessionedMessage : ServiceBusConsumer<SessionMessage>
    {
        protected override Task ProcessMessageAsync(SessionMessage message, ServiceBusReceivedMessage originalMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [ServiceBusQueue(RequireSession = true)]  // RequireSession = true, but message does NOT implement ISessionedEvent
    private sealed class SessionConsumerForNonSessionedMessage : ServiceBusConsumer<QueueMessage>
    {
        protected override Task ProcessMessageAsync(QueueMessage message, ServiceBusReceivedMessage originalMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // GetConsumerMessageType — multi-level inheritance
    private abstract class GenericConsumerBase<T> : ServiceBusConsumer<T> where T : class, IIntegrationEvent;

    [ServiceBusQueue]
    private sealed class DeeplyNestedConsumer : GenericConsumerBase<QueueMessage>
    {
        protected override Task ProcessMessageAsync(QueueMessage message, ServiceBusReceivedMessage originalMessage, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
