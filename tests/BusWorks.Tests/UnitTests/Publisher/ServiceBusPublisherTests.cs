using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Events;
using BusWorks.Publisher;
using Shouldly;
using Xunit;

namespace BusWorks.Tests.UnitTests.Publisher;

/// <summary>
/// Unit tests for <see cref="ServiceBusPublisher.ResolveDestination{TEvent}"/>.
/// <para>
/// <see cref="ServiceBusPublisher"/> cannot be fully unit-tested because
/// <see cref="Azure.Messaging.ServiceBus.ServiceBusClient"/> is sealed and performs I/O
/// on first use. These tests target the static routing logic directly, which is the only
/// independently testable path in the publisher.
/// </para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class ServiceBusPublisherTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    [QueueRoute("orders-queue")]
    private sealed record QueueRoutedEvent(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;

    [TopicRoute("order-events")]
    private sealed record TopicRoutedEvent(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;

    /// <summary>No [QueueRoute] or [TopicRoute] — ResolveDestination must throw.</summary>
    private sealed record UnroutedEvent(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;

    // ── Queue routing ─────────────────────────────────────────────────────────

    [Fact]
    public void ResolveDestination_EventWithQueueRoute_ReturnsQueueName()
    {
        string destination = ServiceBusPublisher.ResolveDestination<QueueRoutedEvent>();

        destination.ShouldBe("orders-queue");
    }

    // ── Topic routing ─────────────────────────────────────────────────────────

    [Fact]
    public void ResolveDestination_EventWithTopicRoute_ReturnsTopicName()
    {
        string destination = ServiceBusPublisher.ResolveDestination<TopicRoutedEvent>();

        destination.ShouldBe("order-events");
    }

    // ── Guard clause ──────────────────────────────────────────────────────────

    [Fact]
    public void ResolveDestination_EventWithNoRouteAttribute_ThrowsInvalidOperationException()
    {
        InvalidOperationException ex =
            Should.Throw<InvalidOperationException>(
                ServiceBusPublisher.ResolveDestination<UnroutedEvent>);

        ex.Message.ShouldContain(nameof(UnroutedEvent));
    }

    [Fact]
    public void ResolveDestination_ExceptionMessage_MentionsQueueRouteAndTopicRoute()
    {
        InvalidOperationException ex =
            Should.Throw<InvalidOperationException>(
                ServiceBusPublisher.ResolveDestination<UnroutedEvent>);

        ex.Message.ShouldContain("QueueRoute");
        ex.Message.ShouldContain("TopicRoute");
    }
}

