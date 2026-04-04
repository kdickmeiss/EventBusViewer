using BusWorks.Abstractions.Attributes;
using Xunit;

namespace BusWorks.Tests.UnitTests.Attributes;

public sealed class ServiceBusQueueAttributeTests{
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
    
    [Fact]
    public void DefaultConstructor_Defaults_AreCorrect()
    {
        // QueueName being null is meaningful — it signals that the queue name should
        // be resolved from [QueueRoute] on the message type at runtime.
        var attr = new ServiceBusQueueAttribute();

        Assert.Null(attr.QueueName);
        Assert.False(attr.RequireSession);
        Assert.Equal(5, attr.MaxDeliveryCount);
    }
    
    [Fact]
    public void ExplicitConstructor_DoesNotTransformQueueName()
    {
        // Verifies the name is stored verbatim — no lowercasing, trimming, or other mutations.
        const string name = "Resort-Created-Events";

        var attr = new ServiceBusQueueAttribute(name);

        Assert.Equal(name, attr.QueueName);
    }
    
    [Fact]
    public void MaxDeliveryCount_CanBeSetToZero_ToDisableEnforcement()
    {
        var attr = new ServiceBusQueueAttribute { MaxDeliveryCount = 0 };

        Assert.Equal(0, attr.MaxDeliveryCount);
    }

    [Fact]
    public void MaxDeliveryCount_NegativeValue_IsStoredAsIs()
    {
        // The attribute itself does not enforce >= 0.
        // That validation is the responsibility of ServiceBusProcessorBackgroundService.ResolveEndpoint.
        var attr = new ServiceBusQueueAttribute { MaxDeliveryCount = -1 };

        Assert.Equal(-1, attr.MaxDeliveryCount);
    }

    [Fact]
    public void ExplicitQueueName_WithInitProperties_AllValuesPreserved()
    {
        var attr = new ServiceBusQueueAttribute("override-queue")
        {
            MaxDeliveryCount = 10,
            RequireSession = true
        };

        Assert.Equal("override-queue", attr.QueueName);
        Assert.Equal(10, attr.MaxDeliveryCount);
        Assert.True(attr.RequireSession);
    }
    
    [Fact]
    public void AttributeUsage_Contract_IsCorrect()
    {
        AttributeUsageAttribute usage = GetAttributeUsage(typeof(ServiceBusQueueAttribute));

        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.Inherited);
        Assert.False(usage.AllowMultiple);
    }
    
    [Fact]
    public void AppliedToClass_WithoutQueueName_AttributeIsPresent()
    {
        ServiceBusQueueAttribute? attr = typeof(ConsumerWithoutQueueName)
            .GetCustomAttributes(typeof(ServiceBusQueueAttribute), inherit: false)
            .Cast<ServiceBusQueueAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Null(attr.QueueName);
    }

    [Fact]
    public void AppliedToClass_WithExplicitQueueName_AttributeIsPresent()
    {
        ServiceBusQueueAttribute? attr = typeof(ConsumerWithExplicitQueueName)
            .GetCustomAttributes(typeof(ServiceBusQueueAttribute), inherit: false)
            .Cast<ServiceBusQueueAttribute>()
            .Single();

        Assert.Equal("explicit-queue", attr.QueueName);
    }

    [Fact]
    public void AppliedToClass_WithSession_AllPropertiesReflected()
    {
        ServiceBusQueueAttribute? attr = typeof(SessionConsumer)
            .GetCustomAttributes(typeof(ServiceBusQueueAttribute), inherit: false)
            .Cast<ServiceBusQueueAttribute>()
            .Single();

        Assert.True(attr.RequireSession);
        Assert.Equal(3, attr.MaxDeliveryCount);
    }

    [Fact]
    public void NotInherited_DerivedClass_DoesNotInheritAttribute()
    {
        ServiceBusQueueAttribute? attr = typeof(DerivedConsumer)
            .GetCustomAttributes(typeof(ServiceBusQueueAttribute), inherit: false)
            .Cast<ServiceBusQueueAttribute>()
            .SingleOrDefault();

        Assert.Null(attr);
    }
    
    [Fact]
    public void MessageType_WithQueueRouteAttribute_QueueNameIsReachable()
    {
        QueueRouteAttribute? routeAttr = typeof(SampleIntegrationEvent)
            .GetCustomAttributes(typeof(QueueRouteAttribute), inherit: false)
            .Cast<QueueRouteAttribute>()
            .Single();

        Assert.Equal("sample-event-queue", routeAttr.QueueName);
    }

    [Fact]
    public void QueueRouteAttribute_AttributeUsage_Contract_IsCorrect()
    {
        AttributeUsageAttribute usage = GetAttributeUsage(typeof(QueueRouteAttribute));

        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.Inherited);
        Assert.False(usage.AllowMultiple);
    }
    
    private static AttributeUsageAttribute GetAttributeUsage(Type attributeType) =>
        attributeType
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();
}
