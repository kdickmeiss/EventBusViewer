using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BusWorks.Attributes;
using OpenTelemetry.Trace;

namespace BusWorks.Publisher;

internal sealed class ServiceBusPublisher(
    ServiceBusClient serviceBusClient,
    Tracer tracer) : IEventBusPublisher, IAsyncDisposable
{
    // ServiceBusSender is thread-safe and designed to be long-lived.
    // Creating one per message adds unnecessary overhead, so we cache by destination name.
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    private ServiceBusSender GetOrCreateSender(string destination) =>
        _senders.GetOrAdd(destination, serviceBusClient.CreateSender);

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        string queueOrTopicName = ResolveDestination<TEvent>();
        ServiceBusSender sender = GetOrCreateSender(queueOrTopicName);

        using TelemetrySpan publishSpan = tracer.StartActiveSpan(
            $"ServiceBus:Publish {queueOrTopicName}",
            SpanKind.Producer);

        // OTel semantic conventions for messaging (matches your consumer side)
        publishSpan.SetAttribute("messaging.system", "azureservicebus");
        publishSpan.SetAttribute("messaging.operation", "publish");
        publishSpan.SetAttribute("messaging.destination.name", queueOrTopicName);
        publishSpan.SetAttribute("messaging.message.id", @event.Id.ToString());

        if (@event is ISessionedEvent sessionedEvent)
            publishSpan.SetAttribute("messaging.servicebus.session_id", sessionedEvent.SessionId);

        try
        {
            string messageBody = JsonSerializer.Serialize(@event);

            // If the event declares a session key (ISessionedEvent), set it on the message
            // so the broker routes it to the correct session and guarantees FIFO ordering.
            string? sessionId = (@event as ISessionedEvent)?.SessionId;

            var message = new ServiceBusMessage(messageBody)
            {
                ContentType = "application/json",
                MessageId = @event.Id.ToString(),
                CorrelationId = @event.Id.ToString(),
                SessionId = sessionId
            };

            // Inject trace context into the message so the consumer
            // can link its span back to this one — this is what your
            // consumer already reads via ApplicationProperties["traceparent"]
            if (publishSpan.Context.IsValid)
            {
                message.ApplicationProperties["traceparent"] =
                    $"00-{publishSpan.Context.TraceId}-{publishSpan.Context.SpanId}-01";
            }

            publishSpan.SetAttribute("messaging.message.body.size", messageBody.Length);

            await sender.SendMessageAsync(message, cancellationToken);

            publishSpan.SetStatus(Status.Ok);
        }
        catch (Exception ex)
        {
            publishSpan.RecordException(ex);
            publishSpan.SetStatus(Status.Error.WithDescription(ex.Message));
            throw;
        }
    }

    /// <summary>
    /// Resolves the Service Bus queue or topic name from the route attribute on <typeparamref name="TEvent"/>.
    /// </summary>
    private static string ResolveDestination<TEvent>() where TEvent : IIntegrationEvent
    {
        Type eventType = typeof(TEvent);

        if (eventType.GetCustomAttribute<QueueRouteAttribute>() is { } queueRoute)
            return queueRoute.QueueName;

        if (eventType.GetCustomAttribute<TopicRouteAttribute>() is { } topicRoute)
            return topicRoute.TopicName;

        throw new InvalidOperationException(
            $"Integration event '{eventType.Name}' must have either:\n" +
            $"  - [QueueRoute(\"queue-name\")] for queue routing\n" +
            $"  - [TopicRoute(\"topic-name\")] for topic routing");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (ServiceBusSender sender in _senders.Values)
        {
            await sender.DisposeAsync();
        }
        _senders.Clear();
    }
}
