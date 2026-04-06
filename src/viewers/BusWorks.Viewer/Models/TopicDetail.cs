using Azure.Messaging.ServiceBus.Administration;

namespace BusWorks.Viewer.Models;

/// <summary>
/// Full topic detail including admin properties and per-subscription info.
/// </summary>
public sealed class TopicDetail
{
    public required string         Name                     { get; init; }
    public required EntityStatus   Status                   { get; init; }
    public required TimeSpan       DefaultMessageTimeToLive { get; init; }
    public required bool           EnableBatchedOperations  { get; init; }
    public required bool           EnablePartitioning       { get; init; }
    public required long           MaxSizeInMegabytes       { get; init; }

    public IReadOnlyList<SubscriptionInfo> Subscriptions { get; init; } = [];

    public int TotalActiveMessages     => Subscriptions.Sum(s => s.ActiveMessages);
    public int TotalDeadLetterMessages => Subscriptions.Sum(s => s.DeadLetterMessages);
    public int TotalMessages           => TotalActiveMessages + TotalDeadLetterMessages;

    public static TopicDetail FromProperties(
        TopicProperties props,
        IReadOnlyList<SubscriptionInfo> subscriptions) => new()
    {
        Name                     = props.Name,
        Status                   = props.Status,
        DefaultMessageTimeToLive = props.DefaultMessageTimeToLive,
        EnableBatchedOperations  = props.EnableBatchedOperations,
        EnablePartitioning       = props.EnablePartitioning,
        MaxSizeInMegabytes       = props.MaxSizeInMegabytes,
        Subscriptions            = subscriptions
    };
}

