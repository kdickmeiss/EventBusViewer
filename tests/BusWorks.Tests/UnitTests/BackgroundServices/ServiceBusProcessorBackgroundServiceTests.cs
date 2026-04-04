using BusWorks.BackgroundServices;
using Xunit;

namespace BusWorks.Tests.UnitTests.BackgroundServices;

public sealed partial class ServiceBusProcessorBackgroundServiceTests
{
    [Fact]
    public void ServiceBusEndpoint_Defaults_AreCorrect()
    {
        ServiceBusEndpoint endpoint = new("my-queue");

        Assert.Equal("my-queue", endpoint.QueueOrTopicName);
        Assert.Null(endpoint.SubscriptionName);
        Assert.False(endpoint.RequireSession);
        Assert.Equal(5, endpoint.MaxDeliveryCount);
    }

    [Fact]
    public void ServiceBusEndpoint_IsQueue_And_IsTopic_ReflectSubscriptionPresence()
    {
        ServiceBusEndpoint queue = new("my-queue");
        ServiceBusEndpoint topic = new("my-topic", SubscriptionName: "my-sub");

        Assert.True(queue.IsQueue);
        Assert.False(queue.IsTopic);
        Assert.False(topic.IsQueue);
        Assert.True(topic.IsTopic);
    }

    [Fact]
    public void ResolveEndpoint_QueueConsumer_WithExplicitName_UsesExplicitName()
    {
        ServiceBusEndpoint endpoint = InvokeResolveEndpoint(typeof(ExplicitQueueConsumer));

        Assert.Equal("explicit-queue", endpoint.QueueOrTopicName);
        Assert.True(endpoint.IsQueue);
        Assert.Null(endpoint.SubscriptionName);
    }

    [Fact]
    public void ResolveEndpoint_QueueConsumer_WithImplicitName_ResolvesFromQueueRoute()
    {
        ServiceBusEndpoint endpoint = InvokeResolveEndpoint(typeof(ImplicitQueueConsumer));

        Assert.Equal("order-queue", endpoint.QueueOrTopicName);
        Assert.True(endpoint.IsQueue);
    }

    [Fact]
    public void ResolveEndpoint_QueueConsumer_WithSession_PreservesAllProperties()
    {
        ServiceBusEndpoint endpoint = InvokeResolveEndpoint(typeof(SessionQueueConsumer));

        Assert.Equal("session-queue", endpoint.QueueOrTopicName);
        Assert.True(endpoint.RequireSession);
        Assert.Equal(3, endpoint.MaxDeliveryCount);
    }

    [Fact]
    public void ResolveEndpoint_QueueConsumer_NegativeMaxDeliveryCount_Throws()
    {
        InvalidOperationException? exception = null;
        try { InvokeResolveEndpoint(typeof(NegativeDeliveryCountQueueConsumer)); }
        catch (InvalidOperationException ex) { exception = ex; }

        Assert.NotNull(exception);
        Assert.Contains(nameof(NegativeDeliveryCountQueueConsumer), exception.Message);
    }

    [Fact]
    public void ResolveEndpoint_QueueConsumer_MessageTypeHasTopicRoute_Throws()
    {
        InvalidOperationException? exception = null;
        try { InvokeResolveEndpoint(typeof(QueueConsumerWithTopicMessage)); }
        catch (InvalidOperationException ex) { exception = ex; }

        Assert.NotNull(exception);
        Assert.Contains(nameof(QueueConsumerWithTopicMessage), exception.Message);
    }

    [Fact]
    public void ResolveEndpoint_QueueConsumer_MessageTypeHasNoRoute_ThrowsInvalidOperationException()
    {
        InvalidOperationException? exception = null;
        try { InvokeResolveEndpoint(typeof(QueueConsumerWithUnroutedMessage)); }
        catch (InvalidOperationException ex) { exception = ex; }

        Assert.NotNull(exception);
        Assert.Contains(nameof(QueueConsumerWithUnroutedMessage), exception.Message);
    }

    [Fact]
    public void ResolveEndpoint_TopicConsumer_ResolvesFromTopicRoute()
    {
        ServiceBusEndpoint endpoint = InvokeResolveEndpoint(typeof(TopicConsumer));

        Assert.Equal("park-events", endpoint.QueueOrTopicName);
        Assert.Equal("resort-subscription", endpoint.SubscriptionName);
        Assert.True(endpoint.IsTopic);
    }

    [Fact]
    public void ResolveEndpoint_TopicConsumer_NegativeMaxDeliveryCount_ThrowsInvalidOperationException()
    {
        InvalidOperationException? exception = null;
        try { InvokeResolveEndpoint(typeof(NegativeDeliveryCountTopicConsumer)); }
        catch (InvalidOperationException ex) { exception = ex; }

        Assert.NotNull(exception);
        Assert.Contains(nameof(NegativeDeliveryCountTopicConsumer), exception.Message);
    }

    [Fact]
    public void ResolveEndpoint_TopicConsumer_MessageTypeHasQueueRoute_ThrowsInvalidOperationException()
    {
        InvalidOperationException? exception = null;
        try { InvokeResolveEndpoint(typeof(TopicConsumerWithQueueMessage)); }
        catch (InvalidOperationException ex) { exception = ex; }

        Assert.NotNull(exception);
        Assert.Contains(nameof(TopicConsumerWithQueueMessage), exception.Message);
    }

    [Fact]
    public void ResolveEndpoint_TopicConsumer_MessageTypeHasNoRoute_ThrowsInvalidOperationException()
    {
        InvalidOperationException? exception = null;
        try { InvokeResolveEndpoint(typeof(TopicConsumerWithUnroutedMessage)); }
        catch (InvalidOperationException ex) { exception = ex; }

        Assert.NotNull(exception);
        Assert.Contains(nameof(TopicConsumerWithUnroutedMessage), exception.Message);
    }

    [Fact]
    public void ResolveEndpoint_ConsumerWithNoRoutingAttribute_ThrowsInvalidOperationException()
    {
        InvalidOperationException? exception = null;
        try { InvokeResolveEndpoint(typeof(UnattributedConsumer)); }
        catch (InvalidOperationException ex) { exception = ex; }

        Assert.NotNull(exception);
        Assert.Contains(nameof(UnattributedConsumer), exception.Message);
    }

    [Fact]
    public void ValidateSessionContract_SessionConsumer_WithSessionedMessage_IsValid()
    {
        ServiceBusEndpoint endpoint = new("session-queue", RequireSession: true);

        InvalidOperationException? exception = null;
        try { InvokeValidateSessionContract(typeof(SessionQueueConsumer), endpoint); }
        catch (InvalidOperationException ex) { exception = ex; }

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateSessionContract_NonSessionConsumer_WithNonSessionedMessage_IsValid()
    {
        ServiceBusEndpoint endpoint = new("order-queue", RequireSession: false);

        InvalidOperationException? exception = null;
        try { InvokeValidateSessionContract(typeof(ImplicitQueueConsumer), endpoint); }
        catch (InvalidOperationException ex) { exception = ex; }

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateSessionContract_SessionConsumer_WithNonSessionedMessage_ThrowsInvalidOperationException()
    {
        ServiceBusEndpoint endpoint = new("order-queue", RequireSession: true);

        InvalidOperationException? exception = null;
        try { InvokeValidateSessionContract(typeof(SessionConsumerForNonSessionedMessage), endpoint); }
        catch (InvalidOperationException ex) { exception = ex; }

        Assert.NotNull(exception);
        Assert.Contains(nameof(SessionConsumerForNonSessionedMessage), exception.Message);
    }

    [Fact]
    public void ValidateSessionContract_NonSessionConsumer_WithSessionedMessage_ThrowsInvalidOperationException()
    {
        ServiceBusEndpoint endpoint = new("session-queue", RequireSession: false);

        InvalidOperationException? exception = null;
        try { InvokeValidateSessionContract(typeof(NonSessionConsumerForSessionedMessage), endpoint); }
        catch (InvalidOperationException ex) { exception = ex; }

        Assert.NotNull(exception);
        Assert.Contains(nameof(SessionMessage), exception.Message);
    }

    [Fact]
    public void GetConsumerMessageType_GenericConsumer_ReturnsMessageType()
    {
        Type? messageType = InvokeGetConsumerMessageType(typeof(ImplicitQueueConsumer));

        Assert.Equal(typeof(QueueMessage), messageType);
    }

    [Fact]
    public void GetConsumerMessageType_DeeplyNestedConsumer_ReturnsMessageType()
    {
        Type? messageType = InvokeGetConsumerMessageType(typeof(DeeplyNestedConsumer));

        Assert.Equal(typeof(QueueMessage), messageType);
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
