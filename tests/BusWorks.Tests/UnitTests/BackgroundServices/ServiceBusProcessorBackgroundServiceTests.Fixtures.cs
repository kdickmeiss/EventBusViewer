using BusWorks.Abstractions;
using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Consumer;
using BusWorks.Abstractions.Events;

namespace BusWorks.Tests.UnitTests.BackgroundServices;

public sealed partial class ServiceBusProcessorBackgroundServiceTests
{
    // ── Message types ─────────────────────────────────────────────────────────

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

    // ── Queue consumers ───────────────────────────────────────────────────────

    [ServiceBusQueue]
    private sealed class ImplicitQueueConsumer : IConsumer<QueueMessage>
    {
        public Task Consume(IConsumeContext<QueueMessage> context) => Task.CompletedTask;
    }

    [ServiceBusQueue("explicit-queue")]
    private sealed class ExplicitQueueConsumer : IConsumer<QueueMessage>
    {
        public Task Consume(IConsumeContext<QueueMessage> context) => Task.CompletedTask;
    }

    [ServiceBusQueue(RequireSession = true, MaxDeliveryCount = 3)]
    private sealed class SessionQueueConsumer : IConsumer<SessionMessage>
    {
        public Task Consume(IConsumeContext<SessionMessage> context) => Task.CompletedTask;
    }

    [ServiceBusQueue(MaxDeliveryCount = -1)]
    private sealed class NegativeDeliveryCountQueueConsumer : IConsumer<QueueMessage>
    {
        public Task Consume(IConsumeContext<QueueMessage> context) => Task.CompletedTask;
    }

    [ServiceBusQueue]  // Queue consumer, but message type has [TopicRoute]
    private sealed class QueueConsumerWithTopicMessage : IConsumer<TopicMessage>
    {
        public Task Consume(IConsumeContext<TopicMessage> context) => Task.CompletedTask;
    }

    [ServiceBusQueue]  // Queue consumer, but message type has no route attribute
    private sealed class QueueConsumerWithUnroutedMessage : IConsumer<UnroutedMessage>
    {
        public Task Consume(IConsumeContext<UnroutedMessage> context) => Task.CompletedTask;
    }

    // Consumer with no routing attribute at all
    private sealed class UnattributedConsumer : IConsumer<QueueMessage>
    {
        public Task Consume(IConsumeContext<QueueMessage> context) => Task.CompletedTask;
    }

    // ── Topic consumers ───────────────────────────────────────────────────────

    [ServiceBusTopic("resort-subscription")]
    private sealed class TopicConsumer : IConsumer<TopicMessage>
    {
        public Task Consume(IConsumeContext<TopicMessage> context) => Task.CompletedTask;
    }

    [ServiceBusTopic("resort-subscription", MaxDeliveryCount = -1)]
    private sealed class NegativeDeliveryCountTopicConsumer : IConsumer<TopicMessage>
    {
        public Task Consume(IConsumeContext<TopicMessage> context) => Task.CompletedTask;
    }

    [ServiceBusTopic("resort-subscription")]  // Topic consumer, but message type has [QueueRoute]
    private sealed class TopicConsumerWithQueueMessage : IConsumer<QueueMessage>
    {
        public Task Consume(IConsumeContext<QueueMessage> context) => Task.CompletedTask;
    }

    [ServiceBusTopic("resort-subscription")]  // Topic consumer, but message type has no route attribute
    private sealed class TopicConsumerWithUnroutedMessage : IConsumer<UnroutedMessage>
    {
        public Task Consume(IConsumeContext<UnroutedMessage> context) => Task.CompletedTask;
    }

    // ── Session contract mismatch fixtures ────────────────────────────────────

    [ServiceBusQueue]  // RequireSession = false, but message implements ISessionedEvent
    private sealed class NonSessionConsumerForSessionedMessage : IConsumer<SessionMessage>
    {
        public Task Consume(IConsumeContext<SessionMessage> context) => Task.CompletedTask;
    }

    [ServiceBusQueue(RequireSession = true)]  // RequireSession = true, but message does NOT implement ISessionedEvent
    private sealed class SessionConsumerForNonSessionedMessage : IConsumer<QueueMessage>
    {
        public Task Consume(IConsumeContext<QueueMessage> context) => Task.CompletedTask;
    }

    // ── GetConsumerMessageType — multi-level inheritance ──────────────────────

    // S1694: Intentionally abstract-only — exists purely to add an inheritance level so
    // GetConsumerMessageType can be tested against a deeply nested consumer type.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1694:An abstract class should have both abstract and concrete methods",
        Justification = "Test fixture: sole purpose is adding an inheritance level for GetConsumerMessageType resolution tests.")]
    private abstract class GenericConsumerBase<T> : IConsumer<T> where T : class, IIntegrationEvent
    {
        public abstract Task Consume(IConsumeContext<T> context);
    }

    [ServiceBusQueue]
    private sealed class DeeplyNestedConsumer : GenericConsumerBase<QueueMessage>
    {
        public override Task Consume(IConsumeContext<QueueMessage> context) => Task.CompletedTask;
    }
}
