using BusWorks.Abstractions.Attributes;
using Xunit;

namespace BusWorks.Tests.UnitTests.Attributes;

public sealed class ServiceBusTopicAttributeTests
{
    [ServiceBusTopic("theme-park-service")]
    private sealed class ConsumerWithSubscriptionName;

    [ServiceBusTopic("alerts-service", RequireSession = true, MaxDeliveryCount = 3)]
    private sealed class SessionConsumer;

    [ServiceBusTopic("base-subscription")]
    private class BaseConsumer;

    private sealed class DerivedConsumer : BaseConsumer;

    [TopicRoute("park-events")]
    private sealed record ParkIntegrationEvent;
    
    [Fact]
    public void Constructor_DoesNotTransformSubscriptionName()
    {
        // Verifies the name is stored verbatim — no lowercasing, trimming, or other mutations.
        const string name = "Theme-Park-Service";

        var attr = new ServiceBusTopicAttribute(name);

        Assert.Equal(name, attr.SubscriptionName);
    }

    [Fact]
    public void Constructor_Defaults_AreCorrect()
    {
        var attr = new ServiceBusTopicAttribute("my-subscription");

        Assert.False(attr.RequireSession);
        Assert.Equal(5, attr.MaxDeliveryCount);
    }
    
    [Fact]
    public void MaxDeliveryCount_CanBeSetToZero_ToDisableEnforcement()
    {
        var attr = new ServiceBusTopicAttribute("my-subscription") { MaxDeliveryCount = 0 };

        Assert.Equal(0, attr.MaxDeliveryCount);
    }

    [Fact]
    public void MaxDeliveryCount_NegativeValue_IsStoredAsIs()
    {
        // The attribute itself does not enforce >= 0.
        // That validation is the responsibility of ServiceBusProcessorBackgroundService.ResolveEndpoint.
        var attr = new ServiceBusTopicAttribute("my-subscription") { MaxDeliveryCount = -1 };

        Assert.Equal(-1, attr.MaxDeliveryCount);
    }

    [Fact]
    public void WithInitProperties_AllValuesPreserved()
    {
        var attr = new ServiceBusTopicAttribute("my-subscription")
        {
            MaxDeliveryCount = 10,
            RequireSession = true
        };

        Assert.Equal("my-subscription", attr.SubscriptionName);
        Assert.Equal(10, attr.MaxDeliveryCount);
        Assert.True(attr.RequireSession);
    }
    
    [Fact]
    public void AttributeUsage_Contract_IsCorrect()
    {
        AttributeUsageAttribute usage = GetAttributeUsage(typeof(ServiceBusTopicAttribute));

        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.Inherited);
        Assert.False(usage.AllowMultiple);
    }
    
    [Fact]
    public void AppliedToClass_SubscriptionName_IsReachableViaReflection()
    {
        ServiceBusTopicAttribute? attr = typeof(ConsumerWithSubscriptionName)
            .GetCustomAttributes(typeof(ServiceBusTopicAttribute), inherit: false)
            .Cast<ServiceBusTopicAttribute>()
            .Single();

        Assert.Equal("theme-park-service", attr.SubscriptionName);
    }

    [Fact]
    public void AppliedToClass_WithSession_AllPropertiesReflected()
    {
        ServiceBusTopicAttribute? attr = typeof(SessionConsumer)
            .GetCustomAttributes(typeof(ServiceBusTopicAttribute), inherit: false)
            .Cast<ServiceBusTopicAttribute>()
            .Single();

        Assert.Equal("alerts-service", attr.SubscriptionName);
        Assert.True(attr.RequireSession);
        Assert.Equal(3, attr.MaxDeliveryCount);
    }

    [Fact]
    public void NotInherited_DerivedClass_DoesNotInheritAttribute()
    {
        ServiceBusTopicAttribute? attr = typeof(DerivedConsumer)
            .GetCustomAttributes(typeof(ServiceBusTopicAttribute), inherit: false)
            .Cast<ServiceBusTopicAttribute>()
            .SingleOrDefault();

        Assert.Null(attr);
    }
    
    [Fact]
    public void MessageType_WithTopicRouteAttribute_TopicNameIsReachable()
    {
        TopicRouteAttribute? routeAttr = typeof(ParkIntegrationEvent)
            .GetCustomAttributes(typeof(TopicRouteAttribute), inherit: false)
            .Cast<TopicRouteAttribute>()
            .Single();

        Assert.Equal("park-events", routeAttr.TopicName);
    }

    [Fact]
    public void TopicRouteAttribute_AttributeUsage_Contract_IsCorrect()
    {
        AttributeUsageAttribute usage = GetAttributeUsage(typeof(TopicRouteAttribute));

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
