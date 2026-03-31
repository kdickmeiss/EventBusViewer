using BusWorks.Abstractions.Consumer;

namespace BusWorks.Abstractions.Attributes;

/// <summary>
/// Attribute to specify a queue for an <see cref="IConsumer{TMessage}"/>.
/// <para>
/// <b>Recommended usage (no explicit name):</b> omit the queue name and let it be resolved
/// automatically from the <see cref="QueueRouteAttribute"/> on the message type.
/// This avoids duplicating the name across the consumer and the event definition.
/// </para>
/// <para>
/// <b>Override usage (explicit name):</b> pass the name directly when you need to override or
/// when consuming a message type that does not have a route attribute.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Recommended — queue name comes from [QueueRoute] on the message type:
/// [ServiceBusQueue(MaxDeliveryCount = 3)]
/// public class MyConsumer : IConsumer&lt;MyEvent&gt; { ... }
///
/// // Override — explicit name takes precedence:
/// [ServiceBusQueue("my-queue", MaxDeliveryCount = 3)]
/// public class MyConsumer : IConsumer&lt;MyEvent&gt; { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServiceBusQueueAttribute : Attribute
{
    /// <summary>
    /// Optional explicit queue name override.
    /// When <c>null</c> the name is resolved from <see cref="QueueRouteAttribute"/>
    /// on the consumer's message type.
    /// </summary>
    public string? QueueName { get; }

    /// <summary>Queue name resolved from the message type's route attribute (no explicit override).</summary>
    public ServiceBusQueueAttribute() { }

    /// <summary>Explicit queue name override.</summary>
    public ServiceBusQueueAttribute(string queueName) => QueueName = queueName;

    /// <summary>
    /// When true, uses a <see cref="Azure.Messaging.ServiceBus.ServiceBusSessionProcessor"/> instead of a regular processor.
    /// Guarantees FIFO ordering per <c>SessionId</c> and processes sessions concurrently.
    /// The queue MUST have sessions enabled in Azure Service Bus.
    /// </summary>
    public bool RequireSession { get; init; }

    /// <summary>
    /// Maximum number of delivery attempts before the message is dead-lettered.
    /// This is the sole source of truth for the retry budget — the application dead-letters
    /// the message itself once <c>DeliveryCount</c> reaches this value.
    ///
    /// IMPORTANT: All Azure Service Bus queues must be provisioned with a high entity-level
    /// <c>MaxDeliveryCount</c> (e.g. 100) so the broker never interferes before this threshold is reached.
    /// This eliminates the risk of the two values drifting out of sync.
    ///
    /// Defaults to <c>5</c> when not specified.
    /// Set to <c>0</c> to disable code-level enforcement entirely (relies on the Azure entity setting — use with caution).
    /// </summary>
    public int MaxDeliveryCount { get; init; } = 5;
}

