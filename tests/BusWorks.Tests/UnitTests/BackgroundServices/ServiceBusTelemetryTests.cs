using Azure.Messaging.ServiceBus;
using BusWorks.BackgroundServices;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;

namespace BusWorks.Tests.UnitTests.BackgroundServices;

[Trait("Category", "Unit")]
public sealed class ServiceBusTelemetryTests
{
    private static readonly Tracer NoOpTracer =
        TracerProvider.Default.GetTracer("BusWorks.Tests.Telemetry");

    private static readonly ServiceBusTelemetry Telemetry =
        new(NoOpTracer, NullLogger.Instance);

    [Fact]
    public void SetupSpanAttributes_QueueEndpoint_DoesNotThrow()
    {
        using TelemetrySpan span = NoOpTracer.StartActiveSpan("test-setup-queue");
        ServiceBusEndpoint endpoint = new("orders-queue");
        Should.NotThrow(() => ServiceBusTelemetry.SetupSpanAttributes(span, endpoint));
    }

    [Fact]
    public void SetupSpanAttributes_TopicEndpoint_DoesNotThrow()
    {
        using TelemetrySpan span = NoOpTracer.StartActiveSpan("test-setup-topic");
        ServiceBusEndpoint endpoint = new("park-events", SubscriptionName: "analytics-sub");
        Should.NotThrow(() => ServiceBusTelemetry.SetupSpanAttributes(span, endpoint));
    }

    [Fact]
    public void CreateMessageSpan_QueueEndpoint_ReturnsDisposableResult()
    {
        ServiceBusEndpoint endpoint = new("orders-queue");
        ServiceBusReceivedMessage message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: Guid.NewGuid().ToString(),
            deliveryCount: 1);

        using ServiceBusTelemetry.MessageSpanResult result =
            Telemetry.CreateMessageSpan("OrderConsumer", endpoint, message);

        result.Span.ShouldNotBeNull();
        result.EnvelopeTraceparent.ShouldNotBeNullOrWhiteSpace();
        Should.NotThrow(result.Span.Dispose);
    }

    [Fact]
    public void CreateMessageSpan_TopicEndpoint_DoesNotThrow()
    {
        ServiceBusEndpoint endpoint = new("park-events", SubscriptionName: "analytics-sub");
        ServiceBusReceivedMessage message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: Guid.NewGuid().ToString());

        Should.NotThrow(() =>
        {
            using ServiceBusTelemetry.MessageSpanResult result =
                Telemetry.CreateMessageSpan("ParkConsumer", endpoint, message);
        });
    }

    [Fact]
    public void CreateMessageSpan_WithCorrelationId_DoesNotThrow()
    {
        ServiceBusEndpoint endpoint = new("orders-queue");
        ServiceBusReceivedMessage message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: Guid.NewGuid().ToString(),
            correlationId: Guid.NewGuid().ToString());

        Should.NotThrow(() =>
        {
            using ServiceBusTelemetry.MessageSpanResult result =
                Telemetry.CreateMessageSpan("OrderConsumer", endpoint, message);
        });
    }

    [Fact]
    public void CreateMessageSpan_WithTraceparentAndEnqueuedTime_DoesNotThrow()
    {
        ServiceBusEndpoint endpoint = new("orders-queue");
        var props = new Dictionary<string, object>
        {
            ["traceparent"] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
        };
        ServiceBusReceivedMessage message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: Guid.NewGuid().ToString(),
            enqueuedTime: DateTimeOffset.UtcNow,
            properties: props);

        Should.NotThrow(() =>
        {
            using ServiceBusTelemetry.MessageSpanResult result =
                Telemetry.CreateMessageSpan("OrderConsumer", endpoint, message);
        });
    }

    [Fact]
    public void CreateMessageSpan_FirstDelivery_EnvelopeTraceparentIsValidW3C()
    {
        ServiceBusEndpoint endpoint = new("orders-queue");
        ServiceBusReceivedMessage message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: Guid.NewGuid().ToString(),
            deliveryCount: 1);

        using ServiceBusTelemetry.MessageSpanResult result =
            Telemetry.CreateMessageSpan("OrderConsumer", endpoint, message);

        // W3C traceparent format: 00-<32 hex traceId>-<16 hex spanId>-<2 hex flags>
        result.EnvelopeTraceparent.ShouldMatch(@"^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$");
    }

    [Fact]
    public void CreateMessageSpan_Retry_UsesEnvelopeTraceparentFromMessageProperty()
    {
        ServiceBusEndpoint endpoint = new("orders-queue");
        string messageId = Guid.NewGuid().ToString();

        // Simulate first delivery to capture the envelope traceparent.
        ServiceBusReceivedMessage firstMessage = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: messageId,
            deliveryCount: 1);

        string envelopeTraceparent;
        using (ServiceBusTelemetry.MessageSpanResult first =
               Telemetry.CreateMessageSpan("OrderConsumer", endpoint, firstMessage))
        {
            envelopeTraceparent = first.EnvelopeTraceparent;
        }

        // Simulate retry: message now carries the envelope traceparent property.
        var retryProps = new Dictionary<string, object>
        {
            [ServiceBusTelemetry.RetryTraceparentKey] = envelopeTraceparent
        };
        ServiceBusReceivedMessage retryMessage = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: messageId,
            deliveryCount: 2,
            properties: retryProps);

        using ServiceBusTelemetry.MessageSpanResult retryResult =
            Telemetry.CreateMessageSpan("OrderConsumer", endpoint, retryMessage);

        // Retry attempt should carry the same envelope traceparent back to the caller.
        retryResult.EnvelopeTraceparent.ShouldBe(envelopeTraceparent);
    }

    [Fact]
    public void BuildAbandonProperties_ContainsRetryTraceparentKey()
    {
        const string traceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        Dictionary<string, object> props = ServiceBusTelemetry.BuildAbandonProperties(traceparent);

        props.ShouldContainKey(ServiceBusTelemetry.RetryTraceparentKey);
        props[ServiceBusTelemetry.RetryTraceparentKey].ShouldBe(traceparent);
    }

    [Fact]
    public void BuildErrorHandler_ReturnsNonNullDelegate()
    {
        Func<ProcessErrorEventArgs, Task> handler = Telemetry.BuildErrorHandler(
            typeof(ServiceBusTelemetryTests),
            "Queue: orders-queue",
            "ServiceBus:Error");
        handler.ShouldNotBeNull();
    }

    [Fact]
    public async Task BuildErrorHandler_InvokedWithErrorArgs_ReturnsCompletedTask()
    {
        Func<ProcessErrorEventArgs, Task> handler = Telemetry.BuildErrorHandler(
            typeof(ServiceBusTelemetryTests),
            "Queue: orders-queue",
            "ServiceBus:Error");
        var args = new ProcessErrorEventArgs(
            exception: new InvalidOperationException("simulated broker error"),
            errorSource: ServiceBusErrorSource.Receive,
            fullyQualifiedNamespace: "test.servicebus.windows.net",
            entityPath: "orders-queue",
            identifier: "test-processor-id",
            cancellationToken: CancellationToken.None);
        Task result = handler(args);
        await result;
        result.IsCompletedSuccessfully.ShouldBeTrue();
    }
}
