using Azure.Messaging.ServiceBus.Administration;

namespace EventBusViewer.Client.Models;

/// <summary>
/// Full queue detail including admin properties and message counts.
/// </summary>
public sealed class QueueDetail
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public required bool RequiresSession { get; init; }
    public required int MaxDeliveryCount { get; init; }
    public required TimeSpan LockDuration { get; init; }
    public required bool DeadLetteringOnMessageExpiration { get; init; }
    public required TimeSpan DefaultMessageTimeToLive { get; init; }
    public required int ActiveMessages { get; init; }
    public required int DeadLetterMessages { get; init; }

    public int TotalMessages => ActiveMessages + DeadLetterMessages;

    public static QueueDetail FromProperties(QueueProperties props, int activeMessages, int deadLetterMessages) =>
        new()
        {
            Name = props.Name,
            Status = props.Status.ToString(),
            RequiresSession = props.RequiresSession,
            MaxDeliveryCount = props.MaxDeliveryCount,
            LockDuration = props.LockDuration,
            DeadLetteringOnMessageExpiration = props.DeadLetteringOnMessageExpiration,
            DefaultMessageTimeToLive = props.DefaultMessageTimeToLive,
            ActiveMessages = activeMessages,
            DeadLetterMessages = deadLetterMessages
        };
}

