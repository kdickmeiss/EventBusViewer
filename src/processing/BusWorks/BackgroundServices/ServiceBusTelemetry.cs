using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;

namespace BusWorks.BackgroundServices;

internal sealed class ServiceBusTelemetry(Tracer tracer, ILogger logger)
{
    /// <summary>
    /// Application-property key written into a message on <c>AbandonMessageAsync</c> so that
    /// the next delivery attempt — on any pod — can reconstruct the envelope span context and
    /// parent itself under the same trace.  State travels with the message, not with the process.
    /// </summary>
    internal const string RetryTraceparentKey = "busworks-retry-traceparent";

    /// <summary>
    /// Returned by <see cref="CreateMessageSpan"/> to give the caller both the active attempt
    /// span and the envelope traceparent string needed for <see cref="BuildAbandonProperties"/>.
    /// Disposing this struct disposes the underlying <see cref="TelemetrySpan"/>.
    /// </summary>
    internal readonly record struct MessageSpanResult(TelemetrySpan Span, string EnvelopeTraceparent) : IDisposable
    {
        public void Dispose() => Span.Dispose();
    }

    /// <summary>
    /// Returns a <see cref="MessageSpanResult"/> for the current delivery attempt.
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>First delivery</b> — creates a short-lived root envelope span, closes it immediately,
    ///     then creates <c>Attempt:1</c> as a child.  The envelope traceparent is returned so the
    ///     caller can embed it in the message via <see cref="BuildAbandonProperties"/>.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Retry</b> — reads the envelope traceparent from the <see cref="RetryTraceparentKey"/>
    ///     message property written by the previous attempt and creates <c>Attempt:N</c> as a child
    ///     of the original envelope.  Works correctly across pods and process restarts.
    ///   </description></item>
    /// </list>
    /// If the message carries a <c>traceparent</c> application property from the producer,
    /// it is attached as a <see cref="Link"/> so the consumer trace can be correlated back
    /// to the producing trace without being nested under it.
    /// </summary>
    /// <param name="consumerName">The simple type name of the consumer, used as the span name suffix.</param>
    /// <param name="endpoint">The endpoint the message was received from.</param>
    /// <param name="message">The received Service Bus message.</param>
    /// <returns>A <see cref="MessageSpanResult"/> the caller must dispose.</returns>
    public MessageSpanResult CreateMessageSpan(
        string consumerName,
        ServiceBusEndpoint endpoint,
        ServiceBusReceivedMessage message)
    {
        // Extract producer trace context (if any) to attach as a link.
        List<Link>? links = null;
        if (message.ApplicationProperties.TryGetValue("traceparent", out object? traceParentObj)
            && traceParentObj is string traceparent
            && ActivityContext.TryParse(traceparent, traceState: null, out ActivityContext producerContext))
        {
            links = [new Link(new SpanContext(producerContext))];
        }

        // On a retry, a previous attempt wrote the envelope traceparent into the message.
        // Reconstruct that SpanContext so this attempt nests under the original trace —
        // even if it lands on a different pod or after a process restart.
        bool isRetry = message.DeliveryCount > 1
            && message.ApplicationProperties.TryGetValue(RetryTraceparentKey, out object? retryParentObj)
            && retryParentObj is string retryTraceparent
            && ActivityContext.TryParse(retryTraceparent, traceState: null, out ActivityContext retryParentContext);

        SpanContext envelopeContext;
        string envelopeTraceparent;

        if (isRetry)
        {
            // Restore the envelope context from the message property.
            envelopeContext = new SpanContext(retryParentContext);
            envelopeTraceparent = (string)message.ApplicationProperties[RetryTraceparentKey];
        }
        else
        {
            // First delivery: create a short-lived root envelope span that anchors the trace.
            // It is disposed immediately — its SpanContext (TraceId + SpanId) is serialised into
            // the message property so all future attempts can parent to it regardless of pod.
            using TelemetrySpan envelopeSpan = tracer.StartActiveSpan(
                $"ServiceBus:Process:{consumerName}",
                SpanKind.Consumer,
                parentContext: default,
                links: links);

            envelopeSpan.SetAttribute("messaging.system", "azureservicebus");
            envelopeSpan.SetAttribute("messaging.destination.name", endpoint.QueueOrTopicName);
            envelopeSpan.SetAttribute("messaging.message.id", message.MessageId);
            envelopeSpan.SetAttribute("messaging.consumer.name", consumerName);

            SpanContext ctx = envelopeSpan.Context;
            envelopeTraceparent = $"00-{ctx.TraceId}-{ctx.SpanId}-{(byte)ctx.TraceFlags:x2}";
            envelopeContext = ctx;
            // envelopeSpan disposed here — the SpanContext lives on in envelopeTraceparent.
        }

        // Each attempt (including the first) is a direct child of the envelope span,
        // making all attempts siblings in the trace view.
        TelemetrySpan span = tracer.StartActiveSpan(
            $"ServiceBus:Process:{consumerName}:Attempt:{message.DeliveryCount}",
            SpanKind.Consumer,
            parentContext: envelopeContext,
            links: isRetry ? links : null); // links already on envelope for first delivery

        span.SetAttribute("messaging.system", "azureservicebus");
        span.SetAttribute("messaging.operation", "process");
        span.SetAttribute("messaging.destination.name", endpoint.QueueOrTopicName);
        span.SetAttribute("messaging.message.id", message.MessageId);
        span.SetAttribute("messaging.message.body.size", message.Body.ToMemory().Length);
        span.SetAttribute("messaging.servicebus.delivery_count", message.DeliveryCount);
        span.SetAttribute("messaging.consumer.name", consumerName);

        AddOptionalMessageAttributes(span, message, endpoint);

        return new MessageSpanResult(span, envelopeTraceparent);
    }

    /// <summary>
    /// Builds the <c>propertiesToModify</c> dictionary for <c>AbandonMessageAsync</c>.
    /// Embeds the envelope traceparent so the next delivery attempt can reconstruct the
    /// parent span context regardless of which pod picks up the message.
    /// </summary>
    /// <param name="envelopeTraceparent">The envelope traceparent from <see cref="MessageSpanResult"/>.</param>
    public static Dictionary<string, object> BuildAbandonProperties(string envelopeTraceparent) =>
        new() { [RetryTraceparentKey] = envelopeTraceparent };

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
            using TelemetrySpan errorSpan = tracer.StartActiveSpan(
                spanName,
                SpanKind.Consumer,
                parentContext: default);
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
    }

    /// <summary>
    /// Extracts the remote parent span context from the message carrier using the
    /// application's configured <see cref="Propagators.DefaultTextMapPropagator"/>
    /// (W3C TraceContext by default). Using the propagation API means:
    /// <list type="bullet">
    ///   <item>the standard lowercase <c>traceparent</c> key is used automatically;</item>
    ///   <item><c>tracestate</c> and <c>Baggage</c> are extracted alongside it;</item>
    ///   <item>any propagator configured at startup (B3, Jaeger, etc.) works without code changes.</item>
    /// </list>
    /// </summary>
    private static bool TryExtractParentContext(ServiceBusReceivedMessage message, out SpanContext parentContext)
    {
        PropagationContext extracted = Propagators.DefaultTextMapPropagator.Extract(
            default,
            message.ApplicationProperties,
            static (props, key) =>
            {
                if (props.TryGetValue(key, out object? value) && value is string s)
                    return [s];
                return [];
            });

        ActivityContext ctx = extracted.ActivityContext;
        if (ctx.TraceId != default)
        {
            parentContext = new SpanContext(ctx.TraceId, ctx.SpanId, ctx.TraceFlags, isRemote: true);
            return true;
        }

        parentContext = default;
        return false;
    }
}
