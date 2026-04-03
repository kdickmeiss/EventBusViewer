using BusWorks.Abstractions;

namespace BusWorks.BackgroundServices;

internal static class ServiceBusConsumerValidator
{
    /// <summary>
    /// Validates that the session contract is consistent:
    /// <list type="bullet">
    ///   <item>If <c>RequireSession = true</c> the message type must implement <see cref="ISessionedEvent"/>
    ///   so the publisher can always set a <c>SessionId</c>.</item>
    ///   <item>If <c>RequireSession = false</c> the message type must NOT implement <see cref="ISessionedEvent"/>
    ///   — publishing a session-keyed event to a non-session queue would be silently rejected by the broker.</item>
    /// </list>
    /// Both checks fire at application startup so misconfiguration is caught immediately.
    /// </summary>
    /// <param name="consumerType">The consumer class to validate.</param>
    /// <param name="endpoint">The resolved endpoint, used to check <see cref="ServiceBusEndpoint.RequireSession"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>RequireSession</c> and <see cref="ISessionedEvent"/> are out of sync.
    /// </exception>
    public static void ValidateSessionContract(Type consumerType, ServiceBusEndpoint endpoint)
    {
        Type? messageType = ServiceBusEndpointResolver.GetConsumerMessageType(consumerType);
        if (messageType is null) return;

        bool isSessioned = typeof(ISessionedEvent).IsAssignableFrom(messageType);

        if (endpoint.RequireSession && !isSessioned)
            throw new InvalidOperationException(
                $"Consumer '{consumerType.Name}' has RequireSession = true, but message type " +
                $"'{messageType.Name}' does not implement ISessionedEvent. " +
                $"Add ': ISessionedEvent' to '{messageType.Name}' and expose a SessionId property " +
                $"that returns a stable domain key (e.g. customerId, orderId).");

        if (!endpoint.RequireSession && isSessioned)
            throw new InvalidOperationException(
                $"Message type '{messageType.Name}' implements ISessionedEvent (declares a SessionId), " +
                $"but consumer '{consumerType.Name}' does not have RequireSession = true. " +
                $"Either add RequireSession = true to the consumer attribute, " +
                $"or remove ISessionedEvent from '{messageType.Name}'.");
    }
}
