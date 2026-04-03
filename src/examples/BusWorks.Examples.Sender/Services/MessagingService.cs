using Azure.Messaging.ServiceBus;

namespace BusWorks.Examples.Sender.Services;

internal sealed class MessagingService(ServiceBusClient serviceBusClient)
{
    public async Task SendMessageAsync(
        string destination,
        string messageBody,
        CancellationToken cancellationToken = default)
    {
        await using ServiceBusSender sender = serviceBusClient.CreateSender(destination);

        var message = new ServiceBusMessage(messageBody)
        {
            ContentType = "application/json",
            MessageId   = Guid.NewGuid().ToString()
        };

        await sender.SendMessageAsync(message, cancellationToken);
    }
}
