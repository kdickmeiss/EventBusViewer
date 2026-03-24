using Azure.Messaging.ServiceBus.Administration;

namespace EventBusViewer.Client.Models;

public sealed record QueueInfo(string Name, int ActiveMessages, int DeadLetterMessages, EntityStatus Status)
{
    public int TotalMessages => ActiveMessages + DeadLetterMessages;
}
