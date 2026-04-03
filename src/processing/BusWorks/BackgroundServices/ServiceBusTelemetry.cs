using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace BusWorks.BackgroundServices;

internal sealed class ServiceBusTelemetry(Tracer tracer, ILogger logger)
{
    /// <summary>
    /// Starts a new consumer telemetry span for the given message, pre-populated with
    /// OTel messaging semantic convention attributes.
    /// </summary>
    /// <param name="consumerName">The simple type name of the consumer, used as the span name suffix.</param>
    /// <param name="endpoint">The endpoint the message was received from.</param>
    /// <param name="message">The received Service Bus message.</param>
    /// <returns>An active <see cref="TelemetrySpan"/> that the caller must dispose.</returns>
    public TelemetrySpan CreateMessageSpan(
        string consumerName,
        ServiceBusEndpoint endpoint,
        ServiceBusReceivedMessage message)
    {
        TelemetrySpan span = tracer.StartActiveSpan(
            $"ServiceBus:Process:{consumerName}",
            SpanKind.Consumer);

        span.SetAttribute("messaging.system", "azureservicebus");
        span.SetAttribute("messaging.operation", "process");
        span.SetAttribute("messaging.destination.name", endpoint.QueueOrTopicName);
        span.SetAttribute("messaging.message.id", message.MessageId);
        span.SetAttribute("messaging.message.body.size", message.Body.ToMemory().Length);
        span.SetAttribute("messaging.servicebus.delivery_count", message.DeliveryCount);
        span.SetAttribute("messaging.consumer.name", consumerName);

        AddOptionalMessageAttributes(span, message, endpoint);

        return span;
    }

    /// <summary>
    /// Records a message processing failure on the span and emits a structured error log.
    /// </summary>
    /// <param name="messageSpan">The active span for this message.</param>
    /// <param name="args">The event args from the Service Bus processor.</param>
    /// <param name="ex">The exception that caused the failure.</param>
    /// <param name="endpointDescription">Human-readable endpoint string for the log message.</param>
    public void HandleMessageProcessingError(
        TelemetrySpan messageSpan,
        ProcessMessageEventArgs args,
        Exception ex,
        string endpointDescription)
    {
        logger.LogError(
            ex,
            "Error processing message from {Endpoint} with MessageId: {MessageId}",
            endpointDescription,
            args.Message.MessageId);

        messageSpan.RecordException(ex);
        messageSpan.SetStatus(Status.Error.WithDescription(ex.Message));
    }

    /// <summary>
    /// Builds a <see cref="ProcessErrorEventArgs"/> handler that records the broker-level error
    /// on a new telemetry span and emits a structured error log.
    /// </summary>
    /// <param name="consumerType">The consumer type, used as a span attribute.</param>
    /// <param name="endpointDescription">Human-readable endpoint string for the log message.</param>
    /// <param name="spanName">The name to give the error span (e.g. <c>"ServiceBus:Error"</c>).</param>
    /// <returns>A delegate suitable for assigning to <c>ProcessErrorAsync</c>.</returns>
    public Func<ProcessErrorEventArgs, Task> BuildErrorHandler(
        Type consumerType,
        string endpointDescription,
        string spanName)
    {
        return args =>
        {
            using TelemetrySpan errorSpan = tracer.StartActiveSpan(spanName);
            errorSpan.SetAttribute("servicebus.error.source", args.ErrorSource.ToString());
            errorSpan.SetAttribute("servicebus.entity.path", args.EntityPath);
            errorSpan.SetAttribute("servicebus.consumer.type", consumerType.Name);
            errorSpan.RecordException(args.Exception);
            errorSpan.SetStatus(Status.Error.WithDescription(args.Exception.Message));

            logger.LogError(
                args.Exception,
                "ServiceBus processor error for {Endpoint}. Error Source: {ErrorSource}",
                endpointDescription,
                args.ErrorSource);

            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Annotates a consumer setup span with endpoint-level attributes (type, name, session, subscription).
    /// Called once per consumer at startup.
    /// </summary>
    /// <param name="span">The span to annotate.</param>
    /// <param name="endpoint">The resolved endpoint.</param>
    public static void SetupSpanAttributes(TelemetrySpan span, ServiceBusEndpoint endpoint)
    {
        span.SetAttribute("servicebus.endpoint.type", endpoint.IsQueue ? "queue" : "topic");
        span.SetAttribute("servicebus.endpoint.name", endpoint.QueueOrTopicName);
        span.SetAttribute("servicebus.session.required", endpoint.RequireSession);
        if (endpoint.IsTopic)
            span.SetAttribute("servicebus.subscription.name", endpoint.SubscriptionName);
    }

    private static void AddOptionalMessageAttributes(
        TelemetrySpan span,
        ServiceBusReceivedMessage message,
        ServiceBusEndpoint endpoint)
    {
        if (!string.IsNullOrEmpty(message.CorrelationId))
            span.SetAttribute("messaging.message.correlation_id", message.CorrelationId);

        if (endpoint.IsTopic)
            span.SetAttribute("messaging.servicebus.subscription.name", endpoint.SubscriptionName);

        if (message.EnqueuedTime != default)
            span.SetAttribute("messaging.message.enqueued_time", message.EnqueuedTime.ToString("o"));

        if (message.ApplicationProperties.TryGetValue("traceparent", out object? traceParent))
            span.SetAttribute("messaging.trace.parent", traceParent.ToString());
    }
}
