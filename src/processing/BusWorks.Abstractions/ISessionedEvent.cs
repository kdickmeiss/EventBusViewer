namespace BusWorks.Abstractions;

/// <summary>
/// Opt-in interface for integration events that require FIFO ordering per session.
/// </summary>
/// <remarks>
/// The event type declares its own grouping key — the publisher reads it automatically.
/// No session ID needs to be passed at the call site, and no changes are required
/// to <see cref="IEventBusPublisher"/>.
/// <para>
/// The value returned by <see cref="SessionId"/> is used as the Azure Service Bus
/// <c>SessionId</c> on the outgoing message. All messages sharing the same value are
/// delivered to one consumer at a time, in strict FIFO order.
/// Different session IDs are processed concurrently.
/// </para>
/// <para>
/// REQUIREMENTS:
/// <list type="bullet">
///   <item>The target queue or topic subscription MUST have sessions enabled in Azure Service Bus.</item>
///   <item>The consumer MUST set <c>RequireSession = true</c> on its <c>[ServiceBusQueue]</c> or <c>[ServiceBusTopic]</c> attribute.</item>
///   <item>The session key must be a stable, domain-meaningful value (e.g. customerId, orderId) —
///   NOT the event's own <c>Id</c>, which is unique per message and would place every message
///   in its own session, defeating the purpose.</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public record PaymentCommand(Guid Id, DateTime OccurredOnUtc, string CustomerId, decimal Amount)
///     : IntegrationEvent(Id, OccurredOnUtc), ISessionedEvent
/// {
///     // All payments for the same customer are ordered. Different customers run concurrently.
///     public string SessionId => CustomerId;
/// }
/// </code>
/// </example>
public interface ISessionedEvent : IIntegrationEvent
{
    /// <summary>
    /// The value that groups related messages into the same session (e.g. customerId, orderId).
    /// All messages with the same <see cref="SessionId"/> are delivered to one consumer at a time, in order.
    /// </summary>
    string SessionId { get; }
}

