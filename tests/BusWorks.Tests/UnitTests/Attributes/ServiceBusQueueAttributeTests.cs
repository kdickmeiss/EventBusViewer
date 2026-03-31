using BusWorks.Abstractions.Attributes;

namespace BusWorks.Tests.UnitTests.Attributes;

internal sealed class ServiceBusQueueAttributeTests
{
    [ServiceBusQueue]
    private sealed class ConsumerWithoutQueueName;

    [ServiceBusQueue("explicit-queue")]
    private sealed class ConsumerWithExplicitQueueName;

    [ServiceBusQueue(RequireSession = true, MaxDeliveryCount = 3)]
    private sealed class SessionConsumer;

    [ServiceBusQueue]
    private class BaseConsumer;

    private sealed class DerivedConsumer : BaseConsumer;

    [QueueRoute("sample-event-queue")]
    private sealed record SampleIntegrationEvent;
    
    [Test]
    public async Task DefaultConstructor_Defaults_AreCorrect()
    {
        // QueueName being null is meaningful — it signals that the queue name should
        // be resolved from [QueueRoute] on the message type at runtime.
        var attr = new ServiceBusQueueAttribute();

        await Assert.That(attr.QueueName).IsNull();
        await Assert.That(attr.RequireSession).IsFalse();
        await Assert.That(attr.MaxDeliveryCount).IsEqualTo(5);
    }
    
    [Test]
    public async Task ExplicitConstructor_DoesNotTransformQueueName()
    {
        // Verifies the name is stored verbatim — no lowercasing, trimming, or other mutations.
        const string name = "Resort-Created-Events";

        var attr = new ServiceBusQueueAttribute(name);

        await Assert.That(attr.QueueName).IsEqualTo(name);
    }
    
    [Test]
    public async Task MaxDeliveryCount_CanBeSetToZero_ToDisableEnforcement()
    {
        var attr = new ServiceBusQueueAttribute { MaxDeliveryCount = 0 };

        await Assert.That(attr.MaxDeliveryCount).IsEqualTo(0);
    }

    [Test]
    public async Task MaxDeliveryCount_NegativeValue_IsStoredAsIs()
    {
        // The attribute itself does not enforce >= 0.
        // That validation is the responsibility of ServiceBusProcessorBackgroundService.ResolveEndpoint.
        var attr = new ServiceBusQueueAttribute { MaxDeliveryCount = -1 };

        await Assert.That(attr.MaxDeliveryCount).IsEqualTo(-1);
    }

    [Test]
    public async Task ExplicitQueueName_WithInitProperties_AllValuesPreserved()
    {
        var attr = new ServiceBusQueueAttribute("override-queue")
        {
            MaxDeliveryCount = 10,
            RequireSession = true
        };

        await Assert.That(attr.QueueName).IsEqualTo("override-queue");
        await Assert.That(attr.MaxDeliveryCount).IsEqualTo(10);
        await Assert.That(attr.RequireSession).IsTrue();
    }
    
    [Test]
    public async Task AttributeUsage_Contract_IsCorrect()
    {
        AttributeUsageAttribute usage = GetAttributeUsage(typeof(ServiceBusQueueAttribute));

        await Assert.That(usage.ValidOn).IsEqualTo(AttributeTargets.Class);
        await Assert.That(usage.Inherited).IsFalse();
        await Assert.That(usage.AllowMultiple).IsFalse();
    }
    
    [Test]
    public async Task AppliedToClass_WithoutQueueName_AttributeIsPresent()
    {
        ServiceBusQueueAttribute? attr = typeof(ConsumerWithoutQueueName)
            .GetCustomAttributes(typeof(ServiceBusQueueAttribute), inherit: false)
            .Cast<ServiceBusQueueAttribute>()
            .SingleOrDefault();

        await Assert.That(attr).IsNotNull();
        await Assert.That(attr!.QueueName).IsNull();
    }

    [Test]
    public async Task AppliedToClass_WithExplicitQueueName_AttributeIsPresent()
    {
        ServiceBusQueueAttribute? attr = typeof(ConsumerWithExplicitQueueName)
            .GetCustomAttributes(typeof(ServiceBusQueueAttribute), inherit: false)
            .Cast<ServiceBusQueueAttribute>()
            .Single();

        await Assert.That(attr.QueueName).IsEqualTo("explicit-queue");
    }

    [Test]
    public async Task AppliedToClass_WithSession_AllPropertiesReflected()
    {
        ServiceBusQueueAttribute? attr = typeof(SessionConsumer)
            .GetCustomAttributes(typeof(ServiceBusQueueAttribute), inherit: false)
            .Cast<ServiceBusQueueAttribute>()
            .Single();

        await Assert.That(attr.RequireSession).IsTrue();
        await Assert.That(attr.MaxDeliveryCount).IsEqualTo(3);
    }

    [Test]
    public async Task NotInherited_DerivedClass_DoesNotInheritAttribute()
    {
        ServiceBusQueueAttribute? attr = typeof(DerivedConsumer)
            .GetCustomAttributes(typeof(ServiceBusQueueAttribute), inherit: false)
            .Cast<ServiceBusQueueAttribute>()
            .SingleOrDefault();

        await Assert.That(attr).IsNull();
    }
    
    [Test]
    public async Task MessageType_WithQueueRouteAttribute_QueueNameIsReachable()
    {
        QueueRouteAttribute? routeAttr = typeof(SampleIntegrationEvent)
            .GetCustomAttributes(typeof(QueueRouteAttribute), inherit: false)
            .Cast<QueueRouteAttribute>()
            .Single();

        await Assert.That(routeAttr.QueueName).IsEqualTo("sample-event-queue");
    }

    [Test]
    public async Task QueueRouteAttribute_AttributeUsage_Contract_IsCorrect()
    {
        AttributeUsageAttribute usage = GetAttributeUsage(typeof(QueueRouteAttribute));

        await Assert.That(usage.ValidOn).IsEqualTo(AttributeTargets.Class);
        await Assert.That(usage.Inherited).IsFalse();
        await Assert.That(usage.AllowMultiple).IsFalse();
    }
    
    private static AttributeUsageAttribute GetAttributeUsage(Type attributeType) =>
        attributeType
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();
}
