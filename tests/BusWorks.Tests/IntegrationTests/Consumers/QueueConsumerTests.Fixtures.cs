using BusWorks.Abstractions;
using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Consumer;
using BusWorks.Abstractions.Events;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;

namespace BusWorks.Tests.IntegrationTests.Consumers;

public sealed partial class QueueConsumerTests
{
    /// <summary>
    /// Domain-realistic event used as the message type for all queue consumer tests.
    /// The <see cref="QueueRouteAttribute"/> is the single source of truth for the queue name:
    /// <see cref="IEventBusPublisher"/> reads it when sending, and <see cref="ServiceBusQueueAttribute"/>
    /// on <see cref="CapturingParkingReservationConsumer"/> resolves it for consumer setup.
    /// </summary>
    [QueueRoute(QueueName)]
    public sealed record ParkingReservationCreatedEvent(
        Guid Id,
        DateTime OccurredOnUtc,
        string SpotCode,
        string UserId,
        decimal HourlyRate) : IntegrationEvent(Id, OccurredOnUtc);

    /// <summary>
    /// Test consumer that writes every processed event into a <see cref="TestConsumerCapture{T}"/>
    /// so tests can <c>await</c> the captured result without a per-invocation
    /// <see cref="System.Threading.Tasks.TaskCompletionSource{T}"/>.
    /// </summary>
    /// <remarks>
    /// Resolved from the DI container via <c>ServiceBusMessageProcessorBuilder.Build()</c>
    /// — the same factory the <c>ServiceBusProcessorBackgroundService</c> uses in production.
    /// </remarks>
    [ServiceBusQueue]
    internal sealed class CapturingParkingReservationConsumer(
        TestConsumerCapture<ParkingReservationCreatedEvent> capture)
        : IConsumer<ParkingReservationCreatedEvent>
    {
        public Task Consume(IConsumeContext<ParkingReservationCreatedEvent> context)
            => capture.WriteAsync(context.Message).AsTask();
    }
}

