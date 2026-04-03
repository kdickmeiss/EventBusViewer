using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Consumer;
using BusWorks.Examples.IntegrationEvents;

namespace BusWorks.Examples.Receiver.Consumers;

[ServiceBusQueue]
internal sealed class ParkingSpotReservedConsumer(
    ILogger<ParkingSpotReservedConsumer> logger) : IConsumer<ParkingSpotReservedIntegrationEvent>
{
    public Task Consume(IConsumeContext<ParkingSpotReservedIntegrationEvent> context)
    {
        logger.LogInformation("Parking spot reserved for license plate: {LicensePlate}", context.Message.LicensePlate);
        return Task.CompletedTask;
    }
}
