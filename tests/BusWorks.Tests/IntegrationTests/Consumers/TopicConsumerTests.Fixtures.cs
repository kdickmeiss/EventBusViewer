using BusWorks.Abstractions;
using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Consumer;
using BusWorks.Abstractions.Events;
using BusWorks.Consumer;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;

namespace BusWorks.Tests.IntegrationTests.Consumers;

public sealed partial class TopicConsumerTests
{
    /// <summary>
    /// Represents a parking-spot status change broadcast to all interested services.
    /// Topics suit this pattern well — reservations, notifications, and analytics
    /// can each subscribe independently without knowing about each other.
    /// The <see cref="TopicRouteAttribute"/> is the single source of truth for the topic
    /// name: <see cref="IEventBusPublisher"/> reads it when sending, and
    /// <see cref="ServiceBusTopicAttribute"/> on <see cref="CapturingParkingSpotConsumer"/>
    /// references it for consumer setup.
    /// </summary>
    [TopicRoute(TopicName)]
    public sealed record ParkingSpotStatusChangedEvent(
        Guid Id,
        DateTime OccurredOnUtc,
        string SpotCode,
        string Status,
        string ParkingLotId) : IntegrationEvent(Id, OccurredOnUtc);

    /// <summary>
    /// Test consumer that writes every processed event into a <see cref="TestConsumerCapture{T}"/>
    /// so tests can <c>await</c> the captured result without a per-invocation
    /// <see cref="System.Threading.Tasks.TaskCompletionSource{T}"/>.
    /// </summary>
    /// <remarks>
    /// Resolved from the DI container via <c>ServiceBusMessageProcessorBuilder.Build()</c>
    /// — the same factory the <c>ServiceBusProcessorBackgroundService</c> uses in production.
    /// </remarks>
    [ServiceBusTopic(SubscriptionName)]
    internal sealed class CapturingParkingSpotConsumer(
        TestConsumerCapture<ParkingSpotStatusChangedEvent> capture)
        : IConsumer<ParkingSpotStatusChangedEvent>
    {
        public Task Consume(IConsumeContext<ParkingSpotStatusChangedEvent> context)
            => capture.WriteAsync(context.Message, context.Metadata, context.CancellationToken).AsTask();
    }
}
