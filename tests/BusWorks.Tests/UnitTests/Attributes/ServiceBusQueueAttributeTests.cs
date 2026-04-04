using BusWorks.Abstractions.Attributes;
using Shouldly;
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
        var attr = new ServiceBusQueueAttribute();

        attr.QueueName.ShouldBeNull();
        attr.RequireSession.ShouldBeFalse();
        attr.MaxDeliveryCount.ShouldBe(5);
    }
    
    [Fact]
    public void ExplicitConstructor_DoesNotTransformQueueName()
    {
        // Verifies the name is stored verbatim — no lowercasing, trimming, or other mutations.
        const string name = "Resort-Created-Events";

        var attr = new ServiceBusQueueAttribute(name);

        attr.QueueName.ShouldBe(name);
    }
    
    [Fact]
    public void MaxDeliveryCount_CanBeSetToZero_ToDisableEnforcement()
    {
        var attr = new ServiceBusQueueAttribute { MaxDeliveryCount = 0 };

        attr.MaxDeliveryCount.ShouldBe(0);
    }

    [Fact]
    public void MaxDeliveryCount_NegativeValue_IsStoredAsIs()
    {
        // The attribute itself does not enforce >= 0.
        // That validation is the responsibility of ServiceBusProcessorBackgroundService.ResolveEndpoint.
        var attr = new ServiceBusQueueAttribute { MaxDeliveryCount = -1 };

        attr.MaxDeliveryCount.ShouldBe(-1);
    }

    [Fact]
    public void ExplicitQueueName_WithInitProperties_AllValuesPreserved()
    {
        var attr = new ServiceBusQueueAttribute("override-queue")
        {
            MaxDeliveryCount = 10,
            RequireSession = true
        };

        attr.QueueName.ShouldBe("override-queue");
        attr.MaxDeliveryCount.ShouldBe(10);
        attr.RequireSession.ShouldBeTrue();
    }
    
    [Fact]
    public void AttributeUsage_Contract_IsCorrect()
    {
        AttributeUsageAttribute usage = GetAttributeUsage(typeof(ServiceBusQueueAttribute));

        usage.ValidOn.ShouldBe(AttributeTargets.Class);
        usage.Inherited.ShouldBeFalse();
        usage.AllowMultiple.ShouldBeFalse();
    }
    
    [Fact]
    public void AppliedToClass_WithoutQueueName_AttributeIsPresent()
    {
        ServiceBusQueueAttribute? attr = typeof(ConsumerWithoutQueueName)
            .GetCustomAttributes(typeof(ServiceBusQueueAttribute), inherit: false)
            .Cast<ServiceBusQueueAttribute>()
            .SingleOrDefault();

        attr.ShouldNotBeNull();
        attr.QueueName.ShouldBeNull();
    }

    [Fact]
    public void AppliedToClass_WithExplicitQueueName_AttributeIsPresent()
    {
        ServiceBusQueueAttribute? attr = typeof(ConsumerWithExplicitQueueName)
            .GetCustomAttributes(typeof(ServiceBusQueueAttribute), inherit: false)
            .Cast<ServiceBusQueueAttribute>()
            .Single();

        attr.QueueName.ShouldBe("explicit-queue");
    }

    [Fact]
    public void AppliedToClass_WithSession_AllPropertiesReflected()
    {
        ServiceBusQueueAttribute? attr = typeof(SessionConsumer)
            .GetCustomAttributes(typeof(ServiceBusQueueAttribute), inherit: false)
            .Cast<ServiceBusQueueAttribute>()
            .Single();

        attr.RequireSession.ShouldBeTrue();
        attr.MaxDeliveryCount.ShouldBe(3);
    }

    [Fact]
    public void NotInherited_DerivedClass_DoesNotInheritAttribute()
    {
        ServiceBusQueueAttribute? attr = typeof(DerivedConsumer)
            .GetCustomAttributes(typeof(ServiceBusQueueAttribute), inherit: false)
            .Cast<ServiceBusQueueAttribute>()
            .SingleOrDefault();

        attr.ShouldBeNull();
    }
    
    [Fact]
    public void MessageType_WithQueueRouteAttribute_QueueNameIsReachable()
    {
        QueueRouteAttribute? routeAttr = typeof(SampleIntegrationEvent)
            .GetCustomAttributes(typeof(QueueRouteAttribute), inherit: false)
            .Cast<QueueRouteAttribute>()
            .Single();

        routeAttr.QueueName.ShouldBe("sample-event-queue");
    }

    [Fact]
    public void QueueRouteAttribute_AttributeUsage_Contract_IsCorrect()
    {
        AttributeUsageAttribute usage = GetAttributeUsage(typeof(QueueRouteAttribute));

        usage.ValidOn.ShouldBe(AttributeTargets.Class);
        usage.Inherited.ShouldBeFalse();
        usage.AllowMultiple.ShouldBeFalse();
    }
    
    private static AttributeUsageAttribute GetAttributeUsage(Type attributeType) =>
        attributeType
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();
}
