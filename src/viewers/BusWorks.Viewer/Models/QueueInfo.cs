using Azure.Messaging.ServiceBus.Administration;

namespace BusWorks.Viewer.Models;

public sealed record QueueInfo(string Name, int ActiveMessages, int DeadLetterMessages, EntityStatus Status, bool RequiresSession = false)
{
    public int TotalMessages => ActiveMessages + DeadLetterMessages;
}
