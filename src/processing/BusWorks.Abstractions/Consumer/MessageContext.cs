namespace BusWorks.Consumer;

/// <summary>
/// Broker-agnostic metadata for a received message, populated by the framework before
/// <see cref="IConsumer{TMessage}.Consume"/> is called.
/// Keeps consumer code free of any Azure Service Bus SDK dependency.
/// </summary>
public sealed class MessageContext
{
    /// <summary>Unique identifier assigned to the message by the broker.</summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// Session identifier. Set when the queue or topic subscription has sessions enabled;
    /// <see langword="null"/> otherwise.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Application-level correlation identifier, typically used to correlate messages across
    /// distributed systems or to link a reply to its originating request.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Number of times this message has been delivered.
    /// Starts at <c>1</c> on the first delivery and increments on each re-delivery.
    /// </summary>
    public int DeliveryCount { get; init; }

    /// <summary>
    /// Broker-assigned, monotonically increasing sequence number that uniquely identifies
    /// the message within the entity.
    /// </summary>
    public long SequenceNumber { get; init; }

    /// <summary>UTC time at which the message was accepted and enqueued by the broker.</summary>
    public DateTimeOffset EnqueuedTime { get; init; }

    /// <summary>
    /// RFC 2045 MIME content type of the message body (e.g. <c>"application/json"</c>).
    /// <see langword="null"/> when not set by the sender.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Application-defined label or subject, analogous to an e-mail subject line.
    /// <see langword="null"/> when not set by the sender.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// Application-defined key/value pairs attached by the sender.
    /// Never <see langword="null"/>; empty when no properties were set.
    /// </summary>
    public IReadOnlyDictionary<string, object> ApplicationProperties { get; init; }
        = new Dictionary<string, object>();
}

