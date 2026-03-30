using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions;

namespace BusWorks.Consumer;

/// <summary>
/// Base interface for all Service Bus consumers (used for discovery).
/// </summary>
internal interface IServiceBusConsumer
{
    Task ProcessMessageInternalAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Shared JSON deserialization defaults, kept outside the generic type to avoid
/// per-<c>TMessage</c> static instances and repeated reflection-cache builds.
/// </summary>
internal static class ServiceBusConsumerDefaults
{
    internal static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

/// <summary>
/// Generic base class for Azure Service Bus message consumers with automatic JSON deserialization.
/// Inherit from this class to create strongly-typed message processors that will be automatically
/// discovered and started at application startup.
///
/// REQUIRED: Decorate the subclass with one of:
/// - <c>[ServiceBusQueue]</c> or <c>[ServiceBusQueue("queue-name")]</c> for queues
/// - <c>[ServiceBusTopic("subscription-name")]</c> for topic subscriptions
/// </summary>
/// <typeparam name="TMessage">
/// The integration event type to deserialize from the Service Bus message body.
/// Must implement <see cref="IIntegrationEvent"/>.
/// </typeparam>
public abstract class ServiceBusConsumer<TMessage> : IServiceBusConsumer
    where TMessage : class, IIntegrationEvent
{
    /// <summary>
    /// Gets the JSON serializer options used for deserialization.
    /// Override to customize deserialization behavior.
    /// The default instance is cached statically to preserve JsonSerializer's internal reflection cache.
    /// When overriding, declare your options as a <c>static readonly</c> field and return it here.
    /// </summary>
    protected virtual JsonSerializerOptions JsonSerializerOptions => ServiceBusConsumerDefaults.JsonSerializerOptions;

    /// <summary>
    /// Process a deserialized message. This method runs in its own DI scope, so scoped services
    /// can be safely injected via the constructor.
    /// </summary>
    /// <param name="message">The deserialized integration event.</param>
    /// <param name="originalMessage">The raw Service Bus message (for metadata such as <c>MessageId</c>, <c>SessionId</c>, etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected abstract Task ProcessMessageAsync(
        TMessage message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken);

    async Task IServiceBusConsumer.ProcessMessageInternalAsync(
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        TMessage? deserializedMessage = JsonSerializer.Deserialize<TMessage>(
            message.Body.ToMemory().Span,
            JsonSerializerOptions);

        if (deserializedMessage is null)
            throw new InvalidOperationException(
                $"Failed to deserialize message {message.MessageId} to type {typeof(TMessage).Name}");

        await ProcessMessageAsync(deserializedMessage, message, cancellationToken);
    }
}

/// <summary>
/// Non-generic base class for raw Service Bus message consumers (no automatic deserialization).
/// Use this when you need full control over message processing or when dealing with non-JSON /
/// non-integration-event messages.
///
/// REQUIRED: Decorate the subclass with one of:
/// - <c>[ServiceBusQueue("queue-name")]</c> for queues (explicit name always required here)
/// - <c>[ServiceBusTopic("subscription-name")]</c> for topic subscriptions
/// </summary>
public abstract class ServiceBusConsumer : IServiceBusConsumer
{
    /// <summary>
    /// Process an incoming Service Bus message. This method runs in its own DI scope, so scoped
    /// services can be safely injected via the constructor.
    /// </summary>
    protected abstract Task ProcessMessageAsync(
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken);

    Task IServiceBusConsumer.ProcessMessageInternalAsync(
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken) =>
        ProcessMessageAsync(message, cancellationToken);
}

