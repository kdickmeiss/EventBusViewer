using BusWorks.BackgroundServices;
using Shouldly;
using Xunit;

namespace BusWorks.Tests.UnitTests.BackgroundServices;

public sealed partial class ServiceBusProcessorBackgroundServiceTests
{
    [Fact]
    public void ServiceBusEndpoint_Defaults_AreCorrect()
    {
        ServiceBusEndpoint endpoint = new("my-queue");

        endpoint.QueueOrTopicName.ShouldBe("my-queue");
        endpoint.SubscriptionName.ShouldBeNull();
        endpoint.RequireSession.ShouldBeFalse();
        endpoint.MaxDeliveryCount.ShouldBe(5);
    }

    [Fact]
    public void ServiceBusEndpoint_IsQueue_And_IsTopic_ReflectSubscriptionPresence()
    {
        ServiceBusEndpoint queue = new("my-queue");
        ServiceBusEndpoint topic = new("my-topic", SubscriptionName: "my-sub");

        queue.IsQueue.ShouldBeTrue();
        queue.IsTopic.ShouldBeFalse();
        topic.IsQueue.ShouldBeFalse();
        topic.IsTopic.ShouldBeTrue();
    }

    [Fact]
    public void ResolveEndpoint_QueueConsumer_WithExplicitName_UsesExplicitName()
    {
        ServiceBusEndpoint endpoint = InvokeResolveEndpoint(typeof(ExplicitQueueConsumer));

        endpoint.QueueOrTopicName.ShouldBe("explicit-queue");
        endpoint.IsQueue.ShouldBeTrue();
        endpoint.SubscriptionName.ShouldBeNull();
    }

    [Fact]
    public void ResolveEndpoint_QueueConsumer_WithImplicitName_ResolvesFromQueueRoute()
    {
        ServiceBusEndpoint endpoint = InvokeResolveEndpoint(typeof(ImplicitQueueConsumer));

        endpoint.QueueOrTopicName.ShouldBe("order-queue");
        endpoint.IsQueue.ShouldBeTrue();
    }

    [Fact]
    public void ResolveEndpoint_QueueConsumer_WithSession_PreservesAllProperties()
    {
        ServiceBusEndpoint endpoint = InvokeResolveEndpoint(typeof(SessionQueueConsumer));

        endpoint.QueueOrTopicName.ShouldBe("session-queue");
        endpoint.RequireSession.ShouldBeTrue();
        endpoint.MaxDeliveryCount.ShouldBe(3);
    }

    [Fact]
    public void ResolveEndpoint_QueueConsumer_NegativeMaxDeliveryCount_Throws()
    {
        Should.Throw<InvalidOperationException>(() => InvokeResolveEndpoint(typeof(NegativeDeliveryCountQueueConsumer)))
            .Message.ShouldContain(nameof(NegativeDeliveryCountQueueConsumer));
    }

    [Fact]
    public void ResolveEndpoint_QueueConsumer_MessageTypeHasTopicRoute_Throws()
    {
        Should.Throw<InvalidOperationException>(() => InvokeResolveEndpoint(typeof(QueueConsumerWithTopicMessage)))
            .Message.ShouldContain(nameof(QueueConsumerWithTopicMessage));
    }

    [Fact]
    public void ResolveEndpoint_QueueConsumer_MessageTypeHasNoRoute_ThrowsInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(() => InvokeResolveEndpoint(typeof(QueueConsumerWithUnroutedMessage)))
            .Message.ShouldContain(nameof(QueueConsumerWithUnroutedMessage));
    }

    [Fact]
    public void ResolveEndpoint_TopicConsumer_ResolvesFromTopicRoute()
    {
        ServiceBusEndpoint endpoint = InvokeResolveEndpoint(typeof(TopicConsumer));

        endpoint.QueueOrTopicName.ShouldBe("park-events");
        endpoint.SubscriptionName.ShouldBe("resort-subscription");
        endpoint.IsTopic.ShouldBeTrue();
    }

    [Fact]
    public void ResolveEndpoint_TopicConsumer_NegativeMaxDeliveryCount_ThrowsInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(() => InvokeResolveEndpoint(typeof(NegativeDeliveryCountTopicConsumer)))
            .Message.ShouldContain(nameof(NegativeDeliveryCountTopicConsumer));
    }

    [Fact]
    public void ResolveEndpoint_TopicConsumer_MessageTypeHasQueueRoute_ThrowsInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(() => InvokeResolveEndpoint(typeof(TopicConsumerWithQueueMessage)))
            .Message.ShouldContain(nameof(TopicConsumerWithQueueMessage));
    }

    [Fact]
    public void ResolveEndpoint_TopicConsumer_MessageTypeHasNoRoute_ThrowsInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(() => InvokeResolveEndpoint(typeof(TopicConsumerWithUnroutedMessage)))
            .Message.ShouldContain(nameof(TopicConsumerWithUnroutedMessage));
    }

    [Fact]
    public void ResolveEndpoint_ConsumerWithNoRoutingAttribute_ThrowsInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(() => InvokeResolveEndpoint(typeof(UnattributedConsumer)))
            .Message.ShouldContain(nameof(UnattributedConsumer));
    }

    [Fact]
    public void ValidateSessionContract_SessionConsumer_WithSessionedMessage_IsValid()
    {
        ServiceBusEndpoint endpoint = new("session-queue", RequireSession: true);

        Should.NotThrow(() => InvokeValidateSessionContract(typeof(SessionQueueConsumer), endpoint));
    }

    [Fact]
    public void ValidateSessionContract_NonSessionConsumer_WithNonSessionedMessage_IsValid()
    {
        ServiceBusEndpoint endpoint = new("order-queue", RequireSession: false);

        Should.NotThrow(() => InvokeValidateSessionContract(typeof(ImplicitQueueConsumer), endpoint));
    }

    [Fact]
    public void ValidateSessionContract_SessionConsumer_WithNonSessionedMessage_ThrowsInvalidOperationException()
    {
        ServiceBusEndpoint endpoint = new("order-queue", RequireSession: true);

        Should.Throw<InvalidOperationException>(() =>
                InvokeValidateSessionContract(typeof(SessionConsumerForNonSessionedMessage), endpoint))
            .Message.ShouldContain(nameof(SessionConsumerForNonSessionedMessage));
    }

    [Fact]
    public void ValidateSessionContract_NonSessionConsumer_WithSessionedMessage_ThrowsInvalidOperationException()
    {
        ServiceBusEndpoint endpoint = new("session-queue", RequireSession: false);

        Should.Throw<InvalidOperationException>(() =>
                InvokeValidateSessionContract(typeof(NonSessionConsumerForSessionedMessage), endpoint))
            .Message.ShouldContain(nameof(SessionMessage));
    }

    [Fact]
    public void GetConsumerMessageType_GenericConsumer_ReturnsMessageType()
    {
        Type? messageType = InvokeGetConsumerMessageType(typeof(ImplicitQueueConsumer));

        messageType.ShouldBe(typeof(QueueMessage));
    }

    [Fact]
    public void GetConsumerMessageType_DeeplyNestedConsumer_ReturnsMessageType()
    {
        Type? messageType = InvokeGetConsumerMessageType(typeof(DeeplyNestedConsumer));

        messageType.ShouldBe(typeof(QueueMessage));
    }

    // ── GetEndpointDescription ────────────────────────────────────────────────

    [Fact]
    public void GetEndpointDescription_QueueEndpoint_ReturnsQueueFormat()
    {
        ServiceBusEndpoint endpoint = new("my-queue");

        string description = ServiceBusEndpointResolver.GetEndpointDescription(endpoint);

        description.ShouldBe("Queue: my-queue");
    }

    [Fact]
    public void GetEndpointDescription_TopicEndpoint_ReturnsTopicAndSubscriptionFormat()
    {
        ServiceBusEndpoint endpoint = new("my-topic", SubscriptionName: "my-sub");

        string description = ServiceBusEndpointResolver.GetEndpointDescription(endpoint);

        description.ShouldBe("Topic: my-topic, Subscription: my-sub");
    }

    // ── ResolveEndpoint — null messageType (consumer does not implement IConsumer<T>) ─

    [Fact]
    public void ResolveEndpoint_QueueConsumer_WithoutIConsumerImplementation_ThrowsWithConsumerName()
    {
        Should.Throw<InvalidOperationException>(() => InvokeResolveEndpoint(typeof(QueueConsumerWithoutIConsumer)))
            .Message.ShouldContain(nameof(QueueConsumerWithoutIConsumer));
    }

    [Fact]
    public void ResolveEndpoint_TopicConsumer_WithoutIConsumerImplementation_ThrowsWithConsumerName()
    {
        Should.Throw<InvalidOperationException>(() => InvokeResolveEndpoint(typeof(TopicConsumerWithoutIConsumer)))
            .Message.ShouldContain(nameof(TopicConsumerWithoutIConsumer));
    }

    // These thin wrappers call the public static methods on the extracted helper
    // classes directly — no reflection needed after the refactoring.
    private static ServiceBusEndpoint InvokeResolveEndpoint(Type consumerType)
        => ServiceBusEndpointResolver.Resolve(consumerType);

    private static void InvokeValidateSessionContract(Type consumerType, ServiceBusEndpoint endpoint)
        => ServiceBusConsumerValidator.ValidateSessionContract(consumerType, endpoint);

    private static Type? InvokeGetConsumerMessageType(Type consumerType)
        => ServiceBusEndpointResolver.GetConsumerMessageType(consumerType);
}
