using Azure.Messaging.ServiceBus.Administration;

namespace BusWorks.Viewer.Models;

public sealed record SubscriptionInfo(
    string Name,
    int ActiveMessages,
    int DeadLetterMessages,
    EntityStatus Status,
    int MaxDeliveryCount,
    TimeSpan LockDuration,
    bool RequiresSession,
    bool DeadLetteringOnMessageExpiration,
    TimeSpan DefaultMessageTimeToLive,
    bool EnableBatchedOperations)
{
    public int TotalMessages => ActiveMessages + DeadLetterMessages;
}
