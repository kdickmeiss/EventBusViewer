using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Events;

namespace BusWorks.Abstractions.Consumer;

/// <summary>
/// Provides access to the deserialized message and its broker metadata inside a consumer.
/// </summary>
/// <typeparam name="TMessage">The deserialized integration event type.</typeparam>
public interface IConsumeContext<out TMessage> where TMessage : class, IIntegrationEvent
{
    /// <summary>The deserialized integration event.</summary>
    TMessage Message { get; }

    /// <summary>
    /// Broker metadata for the received message: <see cref="MessageContext.MessageId"/>,
    /// <see cref="MessageContext.SessionId"/>, <see cref="MessageContext.CorrelationId"/>,
    /// <see cref="MessageContext.DeliveryCount"/>, and more.
    /// </summary>
    MessageContext Metadata { get; }

    /// <summary>Cancellation token for the processing operation.</summary>
    CancellationToken CancellationToken { get; }
}

/// <summary>
/// Defines a strongly-typed Service Bus message consumer with automatic JSON deserialization.
/// <para>
/// Implement this interface, inject your dependencies via the constructor, and decorate the
/// class with <see cref="ServiceBusQueueAttribute"/> or
/// <see cref="ServiceBusTopicAttribute"/>.
/// The framework handles deserialization, DI scoping, distributed tracing, and error handling.
/// </para>
/// </summary>
/// <typeparam name="TMessage">
/// The integration event type to deserialize from the Service Bus message body.
/// Must implement <see cref="IIntegrationEvent"/>.
/// </typeparam>
/// <example>
/// <code>
/// [ServiceBusQueue("my-queue")]
/// public class MyConsumer : IConsumer&lt;MyEvent&gt;
/// {
///     private readonly IMyService _service;
///
///     public MyConsumer(IMyService service) => _service = service;
///
///     public async Task Consume(IConsumeContext&lt;MyEvent&gt; context)
///     {
///         MyEvent msg = context.Message; // already deserialized ✨
///         await _service.HandleAsync(msg, context.CancellationToken);
///     }
/// }
/// </code>
/// </example>
public interface IConsumer<in TMessage> where TMessage : class, IIntegrationEvent
{
    /// <summary>
    /// Processes the incoming message. Called once per message in its own DI scope,
    /// so scoped services can be safely injected via the constructor.
    /// </summary>
    /// <param name="context">
    /// Provides the deserialized message and broker metadata.
    /// </param>
    Task Consume(IConsumeContext<TMessage> context);
}

