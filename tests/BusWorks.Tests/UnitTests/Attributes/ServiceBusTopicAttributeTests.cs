using BusWorks.Abstractions;
using BusWorks.Attributes;

namespace BusWorks.Tests.UnitTests.Attributes;

internal sealed class ServiceBusTopicAttributeTests
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
    
    [Test]
    public async Task Constructor_DoesNotTransformSubscriptionName()
    {
        // Verifies the name is stored verbatim — no lowercasing, trimming, or other mutations.
        const string name = "Theme-Park-Service";

        var attr = new ServiceBusTopicAttribute(name);

        await Assert.That(attr.SubscriptionName).IsEqualTo(name);
    }

    [Test]
    public async Task Constructor_Defaults_AreCorrect()
    {
        var attr = new ServiceBusTopicAttribute("my-subscription");

        await Assert.That(attr.RequireSession).IsFalse();
        await Assert.That(attr.MaxDeliveryCount).IsEqualTo(5);
    }
    
    [Test]
    public async Task MaxDeliveryCount_CanBeSetToZero_ToDisableEnforcement()
    {
        var attr = new ServiceBusTopicAttribute("my-subscription") { MaxDeliveryCount = 0 };

        await Assert.That(attr.MaxDeliveryCount).IsEqualTo(0);
    }

    [Test]
    public async Task MaxDeliveryCount_NegativeValue_IsStoredAsIs()
    {
        // The attribute itself does not enforce >= 0.
        // That validation is the responsibility of ServiceBusProcessorBackgroundService.ResolveEndpoint.
        var attr = new ServiceBusTopicAttribute("my-subscription") { MaxDeliveryCount = -1 };

        await Assert.That(attr.MaxDeliveryCount).IsEqualTo(-1);
    }

    [Test]
    public async Task WithInitProperties_AllValuesPreserved()
    {
        var attr = new ServiceBusTopicAttribute("my-subscription")
        {
            MaxDeliveryCount = 10,
            RequireSession = true
        };

        await Assert.That(attr.SubscriptionName).IsEqualTo("my-subscription");
        await Assert.That(attr.MaxDeliveryCount).IsEqualTo(10);
        await Assert.That(attr.RequireSession).IsTrue();
    }
    
    [Test]
    public async Task AttributeUsage_Contract_IsCorrect()
    {
        AttributeUsageAttribute usage = GetAttributeUsage(typeof(ServiceBusTopicAttribute));

        await Assert.That(usage.ValidOn).IsEqualTo(AttributeTargets.Class);
        await Assert.That(usage.Inherited).IsFalse();
        await Assert.That(usage.AllowMultiple).IsFalse();
    }
    
    [Test]
    public async Task AppliedToClass_SubscriptionName_IsReachableViaReflection()
    {
        ServiceBusTopicAttribute? attr = typeof(ConsumerWithSubscriptionName)
            .GetCustomAttributes(typeof(ServiceBusTopicAttribute), inherit: false)
            .Cast<ServiceBusTopicAttribute>()
            .Single();

        await Assert.That(attr.SubscriptionName).IsEqualTo("theme-park-service");
    }

    [Test]
    public async Task AppliedToClass_WithSession_AllPropertiesReflected()
    {
        ServiceBusTopicAttribute? attr = typeof(SessionConsumer)
            .GetCustomAttributes(typeof(ServiceBusTopicAttribute), inherit: false)
            .Cast<ServiceBusTopicAttribute>()
            .Single();

        await Assert.That(attr.SubscriptionName).IsEqualTo("alerts-service");
        await Assert.That(attr.RequireSession).IsTrue();
        await Assert.That(attr.MaxDeliveryCount).IsEqualTo(3);
    }

    [Test]
    public async Task NotInherited_DerivedClass_DoesNotInheritAttribute()
    {
        ServiceBusTopicAttribute? attr = typeof(DerivedConsumer)
            .GetCustomAttributes(typeof(ServiceBusTopicAttribute), inherit: false)
            .Cast<ServiceBusTopicAttribute>()
            .SingleOrDefault();

        await Assert.That(attr).IsNull();
    }
    
    [Test]
    public async Task MessageType_WithTopicRouteAttribute_TopicNameIsReachable()
    {
        TopicRouteAttribute? routeAttr = typeof(ParkIntegrationEvent)
            .GetCustomAttributes(typeof(TopicRouteAttribute), inherit: false)
            .Cast<TopicRouteAttribute>()
            .Single();

        await Assert.That(routeAttr.TopicName).IsEqualTo("park-events");
    }

    [Test]
    public async Task TopicRouteAttribute_AttributeUsage_Contract_IsCorrect()
    {
        AttributeUsageAttribute usage = GetAttributeUsage(typeof(TopicRouteAttribute));

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
