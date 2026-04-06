using BusWorks.Abstractions.Attributes;
using Shouldly;
using Xunit;

namespace BusWorks.Tests.UnitTests.Attributes;

[Trait("Category", "Unit")]
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

        attr.SubscriptionName.ShouldBe(name);
    }

    [Fact]
    public void Constructor_Defaults_AreCorrect()
    {
        var attr = new ServiceBusTopicAttribute("my-subscription");

        attr.RequireSession.ShouldBeFalse();
        attr.MaxDeliveryCount.ShouldBe(5);
    }

    [Fact]
    public void MaxDeliveryCount_CanBeSetToZero_ToDisableEnforcement()
    {
        var attr = new ServiceBusTopicAttribute("my-subscription") { MaxDeliveryCount = 0 };

        attr.MaxDeliveryCount.ShouldBe(0);
    }

    [Fact]
    public void MaxDeliveryCount_NegativeValue_IsStoredAsIs()
    {
        // The attribute itself does not enforce >= 0.
        // That validation is the responsibility of ServiceBusProcessorBackgroundService.ResolveEndpoint.
        var attr = new ServiceBusTopicAttribute("my-subscription") { MaxDeliveryCount = -1 };

        attr.MaxDeliveryCount.ShouldBe(-1);
    }

    [Fact]
    public void WithInitProperties_AllValuesPreserved()
    {
        var attr = new ServiceBusTopicAttribute("my-subscription")
        {
            MaxDeliveryCount = 10,
            RequireSession = true
        };

        attr.SubscriptionName.ShouldBe("my-subscription");
        attr.MaxDeliveryCount.ShouldBe(10);
        attr.RequireSession.ShouldBeTrue();
    }

    [Fact]
    public void AttributeUsage_Contract_IsCorrect()
    {
        AttributeUsageAttribute usage = GetAttributeUsage(typeof(ServiceBusTopicAttribute));

        usage.ValidOn.ShouldBe(AttributeTargets.Class);
        usage.Inherited.ShouldBeFalse();
        usage.AllowMultiple.ShouldBeFalse();
    }

    [Fact]
    public void AppliedToClass_SubscriptionName_IsReachableViaReflection()
    {
        ServiceBusTopicAttribute? attr = typeof(ConsumerWithSubscriptionName)
            .GetCustomAttributes(typeof(ServiceBusTopicAttribute), inherit: false)
            .Cast<ServiceBusTopicAttribute>()
            .Single();

        attr.SubscriptionName.ShouldBe("theme-park-service");
    }

    [Fact]
    public void AppliedToClass_WithSession_AllPropertiesReflected()
    {
        ServiceBusTopicAttribute? attr = typeof(SessionConsumer)
            .GetCustomAttributes(typeof(ServiceBusTopicAttribute), inherit: false)
            .Cast<ServiceBusTopicAttribute>()
            .Single();

        attr.SubscriptionName.ShouldBe("alerts-service");
        attr.RequireSession.ShouldBeTrue();
        attr.MaxDeliveryCount.ShouldBe(3);
    }

    [Fact]
    public void NotInherited_DerivedClass_DoesNotInheritAttribute()
    {
        ServiceBusTopicAttribute? attr = typeof(DerivedConsumer)
            .GetCustomAttributes(typeof(ServiceBusTopicAttribute), inherit: false)
            .Cast<ServiceBusTopicAttribute>()
            .SingleOrDefault();

        attr.ShouldBeNull();
    }

    [Fact]
    public void MessageType_WithTopicRouteAttribute_TopicNameIsReachable()
    {
        TopicRouteAttribute? routeAttr = typeof(ParkIntegrationEvent)
            .GetCustomAttributes(typeof(TopicRouteAttribute), inherit: false)
            .Cast<TopicRouteAttribute>()
            .Single();

        routeAttr.TopicName.ShouldBe("park-events");
    }

    [Fact]
    public void TopicRouteAttribute_AttributeUsage_Contract_IsCorrect()
    {
        AttributeUsageAttribute usage = GetAttributeUsage(typeof(TopicRouteAttribute));

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
