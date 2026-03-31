using System.Reflection;
using System.Runtime.ExceptionServices;
using BusWorks.BackgroundServices;

namespace BusWorks.Tests.UnitTests.BackgroundServices;

internal sealed partial class ServiceBusProcessorBackgroundServiceTests
{
    [Test]
    public async Task ServiceBusEndpoint_Defaults_AreCorrect()
    {
        ServiceBusEndpoint endpoint = new("my-queue");

        await Assert.That(endpoint.QueueOrTopicName).IsEqualTo("my-queue");
        await Assert.That(endpoint.SubscriptionName).IsNull();
        await Assert.That(endpoint.RequireSession).IsFalse();
        await Assert.That(endpoint.MaxDeliveryCount).IsEqualTo(5);
    }

    [Test]
    public async Task ServiceBusEndpoint_IsQueue_And_IsTopic_ReflectSubscriptionPresence()
    {
        ServiceBusEndpoint queue = new("my-queue");
        ServiceBusEndpoint topic = new("my-topic", SubscriptionName: "my-sub");

        await Assert.That(queue.IsQueue).IsTrue();
        await Assert.That(queue.IsTopic).IsFalse();
        await Assert.That(topic.IsQueue).IsFalse();
        await Assert.That(topic.IsTopic).IsTrue();
    }

    [Test]
    public async Task ResolveEndpoint_QueueConsumer_WithExplicitName_UsesExplicitName()
    {
        ServiceBusEndpoint endpoint = InvokeResolveEndpoint(typeof(ExplicitQueueConsumer));

        await Assert.That(endpoint.QueueOrTopicName).IsEqualTo("explicit-queue");
        await Assert.That(endpoint.IsQueue).IsTrue();
        await Assert.That(endpoint.SubscriptionName).IsNull();
    }

    [Test]
    public async Task ResolveEndpoint_QueueConsumer_WithImplicitName_ResolvesFromQueueRoute()
    {
        // The recommended usage — no explicit name on [ServiceBusQueue], resolved from [QueueRoute] on the message type.
        ServiceBusEndpoint endpoint = InvokeResolveEndpoint(typeof(ImplicitQueueConsumer));

        await Assert.That(endpoint.QueueOrTopicName).IsEqualTo("order-queue");
        await Assert.That(endpoint.IsQueue).IsTrue();
    }

    [Test]
    public async Task ResolveEndpoint_QueueConsumer_WithSession_PreservesAllProperties()
    {
        ServiceBusEndpoint endpoint = InvokeResolveEndpoint(typeof(SessionQueueConsumer));

        await Assert.That(endpoint.QueueOrTopicName).IsEqualTo("session-queue");
        await Assert.That(endpoint.RequireSession).IsTrue();
        await Assert.That(endpoint.MaxDeliveryCount).IsEqualTo(3);
    }

    [Test]
    public async Task ResolveEndpoint_QueueConsumer_NegativeMaxDeliveryCount_Throws()
    {
        InvalidOperationException? exception = null;
        try { InvokeResolveEndpoint(typeof(NegativeDeliveryCountQueueConsumer)); }
        catch (InvalidOperationException ex) { exception = ex; }

        await Assert.That(exception).IsNotNull();
        bool containsConsumerName = exception!.Message.Contains(nameof(NegativeDeliveryCountQueueConsumer));
        await Assert.That(containsConsumerName).IsTrue();
    }

    [Test]
    public async Task ResolveEndpoint_QueueConsumer_MessageTypeHasTopicRoute_Throws()
    {
        // [ServiceBusQueue] on a consumer whose message type is declared as a topic — caught at startup.
        InvalidOperationException? exception = null;
        try { InvokeResolveEndpoint(typeof(QueueConsumerWithTopicMessage)); }
        catch (InvalidOperationException ex) { exception = ex; }

        await Assert.That(exception).IsNotNull();
        bool containsConsumerName = exception!.Message.Contains(nameof(QueueConsumerWithTopicMessage));
        await Assert.That(containsConsumerName).IsTrue();
    }

    [Test]
    public async Task ResolveEndpoint_QueueConsumer_MessageTypeHasNoRoute_ThrowsInvalidOperationException()
    {
        InvalidOperationException? exception = null;
        try { InvokeResolveEndpoint(typeof(QueueConsumerWithUnroutedMessage)); }
        catch (InvalidOperationException ex) { exception = ex; }

        await Assert.That(exception).IsNotNull();
        bool containsConsumerName = exception!.Message.Contains(nameof(QueueConsumerWithUnroutedMessage));
        await Assert.That(containsConsumerName).IsTrue();
    }

    [Test]
    public async Task ResolveEndpoint_TopicConsumer_ResolvesFromTopicRoute()
    {
        ServiceBusEndpoint endpoint = InvokeResolveEndpoint(typeof(TopicConsumer));

        await Assert.That(endpoint.QueueOrTopicName).IsEqualTo("park-events");
        await Assert.That(endpoint.SubscriptionName).IsEqualTo("resort-subscription");
        await Assert.That(endpoint.IsTopic).IsTrue();
    }

    [Test]
    public async Task ResolveEndpoint_TopicConsumer_NegativeMaxDeliveryCount_ThrowsInvalidOperationException()
    {
        InvalidOperationException? exception = null;
        try { InvokeResolveEndpoint(typeof(NegativeDeliveryCountTopicConsumer)); }
        catch (InvalidOperationException ex) { exception = ex; }

        await Assert.That(exception).IsNotNull();
        bool containsConsumerName = exception!.Message.Contains(nameof(NegativeDeliveryCountTopicConsumer));
        await Assert.That(containsConsumerName).IsTrue();
    }

    [Test]
    public async Task ResolveEndpoint_TopicConsumer_MessageTypeHasQueueRoute_ThrowsInvalidOperationException()
    {
        // [ServiceBusTopic] on a consumer whose message type is declared as a queue — caught at startup.
        InvalidOperationException? exception = null;
        try { InvokeResolveEndpoint(typeof(TopicConsumerWithQueueMessage)); }
        catch (InvalidOperationException ex) { exception = ex; }

        await Assert.That(exception).IsNotNull();
        bool containsConsumerName = exception!.Message.Contains(nameof(TopicConsumerWithQueueMessage));
        await Assert.That(containsConsumerName).IsTrue();
    }

    [Test]
    public async Task ResolveEndpoint_TopicConsumer_MessageTypeHasNoRoute_ThrowsInvalidOperationException()
    {
        InvalidOperationException? exception = null;
        try { InvokeResolveEndpoint(typeof(TopicConsumerWithUnroutedMessage)); }
        catch (InvalidOperationException ex) { exception = ex; }

        await Assert.That(exception).IsNotNull();
        bool containsConsumerName = exception!.Message.Contains(nameof(TopicConsumerWithUnroutedMessage));
        await Assert.That(containsConsumerName).IsTrue();
    }

    [Test]
    public async Task ResolveEndpoint_ConsumerWithNoRoutingAttribute_ThrowsInvalidOperationException()
    {
        InvalidOperationException? exception = null;
        try { InvokeResolveEndpoint(typeof(UnattributedConsumer)); }
        catch (InvalidOperationException ex) { exception = ex; }

        await Assert.That(exception).IsNotNull();
        bool containsConsumerName = exception!.Message.Contains(nameof(UnattributedConsumer));
        await Assert.That(containsConsumerName).IsTrue();
    }

    [Test]
    public async Task ValidateSessionContract_SessionConsumer_WithSessionedMessage_IsValid()
    {
        // RequireSession = true + ISessionedEvent message → valid configuration.
        ServiceBusEndpoint endpoint = new("session-queue", RequireSession: true);

        InvalidOperationException? exception = null;
        try { InvokeValidateSessionContract(typeof(SessionQueueConsumer), endpoint); }
        catch (InvalidOperationException ex) { exception = ex; }

        await Assert.That(exception).IsNull();
    }

    [Test]
    public async Task ValidateSessionContract_NonSessionConsumer_WithNonSessionedMessage_IsValid()
    {
        // RequireSession = false + non-ISessionedEvent message → valid configuration.
        ServiceBusEndpoint endpoint = new("order-queue", RequireSession: false);

        InvalidOperationException? exception = null;
        try { InvokeValidateSessionContract(typeof(ImplicitQueueConsumer), endpoint); }
        catch (InvalidOperationException ex) { exception = ex; }

        await Assert.That(exception).IsNull();
    }

    [Test]
    public async Task ValidateSessionContract_SessionConsumer_WithNonSessionedMessage_ThrowsInvalidOperationException()
    {
        // RequireSession = true but message does NOT implement ISessionedEvent → caught at startup.
        ServiceBusEndpoint endpoint = new("order-queue", RequireSession: true);

        InvalidOperationException? exception = null;
        try { InvokeValidateSessionContract(typeof(SessionConsumerForNonSessionedMessage), endpoint); }
        catch (InvalidOperationException ex) { exception = ex; }

        await Assert.That(exception).IsNotNull();
        bool containsConsumerName = exception!.Message.Contains(nameof(SessionConsumerForNonSessionedMessage));
        await Assert.That(containsConsumerName).IsTrue();
    }

    [Test]
    public async Task ValidateSessionContract_NonSessionConsumer_WithSessionedMessage_ThrowsInvalidOperationException()
    {
        // ISessionedEvent message but RequireSession = false → caught at startup.
        ServiceBusEndpoint endpoint = new("session-queue", RequireSession: false);

        InvalidOperationException? exception = null;
        try { InvokeValidateSessionContract(typeof(NonSessionConsumerForSessionedMessage), endpoint); }
        catch (InvalidOperationException ex) { exception = ex; }

        await Assert.That(exception).IsNotNull();
        bool containsMessageTypeName = exception!.Message.Contains(nameof(SessionMessage));
        await Assert.That(containsMessageTypeName).IsTrue();
    }

    [Test]
    public async Task GetConsumerMessageType_GenericConsumer_ReturnsMessageType()
    {
        Type? messageType = InvokeGetConsumerMessageType(typeof(ImplicitQueueConsumer));

        await Assert.That(messageType).IsEqualTo(typeof(QueueMessage));
    }


    [Test]
    public async Task GetConsumerMessageType_DeeplyNestedConsumer_ReturnsMessageType()
    {
        // Resolves through multiple inheritance levels — important for real-world consumers
        // that extend an intermediate base class.
        Type? messageType = InvokeGetConsumerMessageType(typeof(DeeplyNestedConsumer));

        await Assert.That(messageType).IsEqualTo(typeof(QueueMessage));
    }
    
    // ── Reflection helpers ────────────────────────────────────────────────────
    //
    // ResolveEndpoint, ValidateSessionContract, and GetConsumerMessageType are private static
    // methods containing the core startup logic. They are pure functions (Type → result) with
    // no Azure SDK dependencies, making them ideal unit test targets despite being private.
    //
    // S3011 — accessibility bypass is intentional and safe here because:
    //   • This is test-only code; the suppression never ships to production.
    //   • The targeted methods are private static pure functions (no instance state, no side
    //     effects, no Azure SDK calls) — there is no security or encapsulation risk.
    //   • They are called only with well-known Type arguments defined as fixtures in this file.
    //   • The alternative (making the methods internal) would widen the production API purely
    //     for test purposes, which is a worse trade-off than a scoped suppression here.
#pragma warning disable S3011
    private static ServiceBusEndpoint InvokeResolveEndpoint(Type consumerType)
    {
        MethodInfo method = typeof(ServiceBusProcessorBackgroundService)
            .GetMethod("ResolveEndpoint", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return (ServiceBusEndpoint)method.Invoke(null, [consumerType])!;
        }
        catch (TargetInvocationException tie)
        {
            ExceptionDispatchInfo.Capture(tie.InnerException!).Throw();
            throw; // unreachable — satisfies the compiler
        }
    }

    private static void InvokeValidateSessionContract(Type consumerType, ServiceBusEndpoint endpoint)
    {
        MethodInfo method = typeof(ServiceBusProcessorBackgroundService)
            .GetMethod("ValidateSessionContract", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            method.Invoke(null, [consumerType, endpoint]);
        }
        catch (TargetInvocationException tie)
        {
            ExceptionDispatchInfo.Capture(tie.InnerException!).Throw();
            throw;
        }
    }

    private static Type? InvokeGetConsumerMessageType(Type consumerType)
    {
        MethodInfo method = typeof(ServiceBusProcessorBackgroundService)
            .GetMethod("GetConsumerMessageType", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Type?)method.Invoke(null, [consumerType]);
    }
#pragma warning restore S3011
}
