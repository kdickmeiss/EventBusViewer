using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Consumer;
using BusWorks.Examples.IntegrationEvents;

namespace BusWorks.Examples.Receiver.Consumers;

[ServiceBusTopic( "email-notifications")]
internal sealed class ParkingTicketBoughtConsumer(ILogger<ParkingTicketBoughtConsumer> logger) : IConsumer<ParkingTicketBoughtIntegrationEvent>
{
    public Task Consume(IConsumeContext<ParkingTicketBoughtIntegrationEvent> context)
    {
        logger.LogInformation("Parking ticket bought for license plate: {LicensePlate}", context.Message.LicensePlate);
        return Task.CompletedTask;
    }
}
