using System.Reflection;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions;
using BusWorks.Abstractions.Consumer;
using BusWorks.Abstractions.Events;
using BusWorks.Consumer;
using Microsoft.Extensions.DependencyInjection;

namespace BusWorks.BackgroundServices;

internal static class ServiceBusMessageProcessorBuilder
{
    // Cached once per app lifetime; Build() calls MakeGenericMethod at consumer-setup time
    // so that neither MakeGenericMethod nor Invoke appear on the per-message hot path.
    private static readonly MethodInfo BuildTypedProcessorMethod =
        typeof(ServiceBusMessageProcessorBuilder)
            .GetMethod(nameof(BuildTypedProcessor), BindingFlags.Public | BindingFlags.Static)!;

    /// <summary>
    /// Builds a scoped factory delegate for the given consumer type.
    /// The returned factory is called once per DI scope (i.e. once per message) and resolves
    /// the consumer from the scope before returning the strongly-typed dispatch delegate.
    /// <see cref="MethodInfo.MakeGenericMethod"/> is invoked here at consumer-setup time so
    /// that neither reflection nor <c>Invoke</c> appear on the per-message hot path.
    /// </summary>
    /// <param name="consumerType">The consumer class to build a factory for.</param>
    /// <returns>
    /// A factory that accepts an <see cref="IServiceProvider"/> and returns an async delegate
    /// that deserialises the raw <see cref="Azure.Messaging.ServiceBus.ServiceBusReceivedMessage"/>
    /// and dispatches it to the consumer.
    /// </returns>
    public static Func<IServiceProvider, Func<ServiceBusReceivedMessage, CancellationToken, Task>>
        Build(Type consumerType)
    {
        Type messageType = ServiceBusEndpointResolver.GetConsumerMessageType(consumerType)!;
        MethodInfo method = BuildTypedProcessorMethod.MakeGenericMethod(messageType);
        return provider =>
        {
            object consumer = provider.GetRequiredService(consumerType);
            return (Func<ServiceBusReceivedMessage, CancellationToken, Task>)method.Invoke(null, [consumer])!;
        };
    }

    /// <summary>
    /// Creates the strongly-typed async dispatch delegate for a single consumer instance.
    /// Deserialises the message body to <typeparamref name="TMessage"/> and calls
    /// <see cref="IConsumer{TMessage}.Consume"/>.
    /// </summary>
    /// <typeparam name="TMessage">The integration event type the consumer handles.</typeparam>
    /// <param name="consumer">The resolved consumer instance.</param>
    /// <returns>An async delegate that processes one <see cref="Azure.Messaging.ServiceBus.ServiceBusReceivedMessage"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when JSON deserialisation returns <c>null</c>.</exception>
    public static Func<ServiceBusReceivedMessage, CancellationToken, Task> BuildTypedProcessor<TMessage>(
        IConsumer<TMessage> consumer)
        where TMessage : class, IIntegrationEvent
    {
        return async (message, cancellationToken) =>
        {
            TMessage? deserialized = JsonSerializer.Deserialize<TMessage>(
                message.Body.ToMemory().Span,
                ServiceBusConsumerDefaults.JsonSerializerOptions);

            if (deserialized is null)
                throw new InvalidOperationException(
                    $"Failed to deserialize message {message.MessageId} to type {typeof(TMessage).Name}");

            MessageContext metadata = ToMessageContext(message);
            await consumer.Consume(new ConsumeContext<TMessage>(deserialized, metadata, cancellationToken));
        };
    }

    private static MessageContext ToMessageContext(ServiceBusReceivedMessage m) => new()
    {
        MessageId = m.MessageId,
        SessionId = m.SessionId,
        CorrelationId = m.CorrelationId,
        DeliveryCount = m.DeliveryCount,
        SequenceNumber = m.SequenceNumber,
        EnqueuedTime = m.EnqueuedTime,
        ContentType = m.ContentType,
        Subject = m.Subject,
        ApplicationProperties = m.ApplicationProperties
    };
}
