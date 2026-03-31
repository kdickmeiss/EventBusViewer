using Azure.Messaging.ServiceBus;

namespace BusWorks.Examples.Sender.Services;

internal sealed class QueueService(ServiceBusClient serviceBusClient)
{
    public async Task SendMessageAsync(
        string queueName,
        string messageBody,
        CancellationToken cancellationToken = default)
    {
        await using ServiceBusSender sender = serviceBusClient.CreateSender(queueName);

        var message = new ServiceBusMessage(messageBody)
        {
            ContentType = "application/json",
            MessageId   = Guid.NewGuid().ToString()
        };

        await sender.SendMessageAsync(message, cancellationToken);
    }
}
