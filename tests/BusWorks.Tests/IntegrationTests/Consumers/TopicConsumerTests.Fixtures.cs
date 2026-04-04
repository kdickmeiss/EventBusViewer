using BusWorks.Abstractions;
using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Consumer;
using BusWorks.Abstractions.Events;
using BusWorks.Consumer;

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
    /// Test-only consumer that signals a <see cref="TaskCompletionSource{T}"/> once the
    /// deserialized message has been processed.
    /// Instantiated directly by test helpers — not via the DI container or the background service.
    /// </summary>
    [ServiceBusTopic(SubscriptionName)]
    internal sealed class CapturingParkingSpotConsumer(
        TaskCompletionSource<ParkingSpotStatusChangedEvent> completion)
        : IConsumer<ParkingSpotStatusChangedEvent>
    {
        public Task Consume(IConsumeContext<ParkingSpotStatusChangedEvent> context)
        {
            completion.TrySetResult(context.Message);
            return Task.CompletedTask;
        }
    }
}

