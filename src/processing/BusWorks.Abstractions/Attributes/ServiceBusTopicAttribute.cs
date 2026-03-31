namespace BusWorks.Attributes;

/// <summary>
/// Attribute to specify a topic subscription for an <see cref="BusWorks.Consumer.IConsumer{TMessage}"/>.
/// <para>
/// The <b>topic name</b> is resolved automatically from the <see cref="TopicRouteAttribute"/>
/// on the consumer's message type — it is the single source of truth.
/// </para>
/// <para>
/// The <b>subscription name</b> is consumer-specific (a topic can have many independent subscribers)
/// and must always be supplied here.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // The topic name comes from [TopicRoute("park-events")] on the message type.
/// [ServiceBusTopic("theme-park-service", MaxDeliveryCount = 5)]
/// public class ParkEventConsumer : IConsumer&lt;ParkEvent&gt; { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServiceBusTopicAttribute(string subscriptionName) : Attribute
{
    /// <summary>
    /// The literal subscription name (used as-is). Required because a single topic can have
    /// many independent subscriptions — each consumer must identify its own.
    /// The topic name itself comes from <see cref="TopicRouteAttribute"/> on the message type.
    /// </summary>
    public string SubscriptionName { get; } = subscriptionName;

    /// <summary>
    /// When true, uses a <see cref="Azure.Messaging.ServiceBus.ServiceBusSessionProcessor"/> instead of a regular processor.
    /// Guarantees FIFO ordering per <c>SessionId</c> and processes sessions concurrently.
    /// The topic subscription MUST have sessions enabled in Azure Service Bus.
    /// </summary>
    public bool RequireSession { get; init; }

    /// <summary>
    /// Maximum number of delivery attempts before the message is dead-lettered.
    /// This is the sole source of truth for the retry budget — the application dead-letters
    /// the message itself once <c>DeliveryCount</c> reaches this value.
    ///
    /// IMPORTANT: All Azure Service Bus topic subscriptions must be provisioned with a high entity-level
    /// <c>MaxDeliveryCount</c> (e.g. 100) so the broker never interferes before this threshold is reached.
    /// This eliminates the risk of the two values drifting out of sync.
    ///
    /// Defaults to <c>5</c> when not specified.
    /// Set to <c>0</c> to disable code-level enforcement entirely (relies on the Azure entity setting — use with caution).
    /// </summary>
    public int MaxDeliveryCount { get; init; } = 5;
}

