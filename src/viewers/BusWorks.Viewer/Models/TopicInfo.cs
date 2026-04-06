using Azure.Messaging.ServiceBus.Administration;

namespace BusWorks.Viewer.Models;

public sealed record TopicInfo(
    string Name,
    int SubscriptionCount,
    int ActiveMessages,
    int DeadLetterMessages,
    EntityStatus Status)
{
    public int TotalMessages => ActiveMessages + DeadLetterMessages;
}
