using BusWorks.Abstractions;
using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Consumer;
using BusWorks.Abstractions.Events;

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
    /// Test-only consumer that signals a <see cref="TaskCompletionSource{T}"/> once the
    /// deserialized message has been processed.
    /// </summary>
    /// <remarks>
    /// Instantiated directly by the test helpers — <b>not</b> via the DI container or the
    /// background service. The <see cref="ServiceBusQueueAttribute"/> is declared so the class
    /// is correctly attributed (matching what production consumers look like) and so the
    /// consumer-discovery invariant tests continue to pass.
    /// </remarks>
    [ServiceBusQueue]
    internal sealed class CapturingParkingReservationConsumer(
        TaskCompletionSource<ParkingReservationCreatedEvent> completion)
        : IConsumer<ParkingReservationCreatedEvent>
    {
        public Task Consume(IConsumeContext<ParkingReservationCreatedEvent> context)
        {
            completion.TrySetResult(context.Message);
            return Task.CompletedTask;
        }
    }
}

