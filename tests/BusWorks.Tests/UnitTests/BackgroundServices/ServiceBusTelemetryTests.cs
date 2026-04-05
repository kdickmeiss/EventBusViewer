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
    public void CreateMessageSpan_QueueEndpoint_ReturnsDisposableSpan()
    {
        ServiceBusEndpoint endpoint = new("orders-queue");
        ServiceBusReceivedMessage message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: Guid.NewGuid().ToString(),
            deliveryCount: 1);
        TelemetrySpan span = Telemetry.CreateMessageSpan("OrderConsumer", endpoint, message);
        Should.NotThrow(span.Dispose);
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
            TelemetrySpan span = Telemetry.CreateMessageSpan("ParkConsumer", endpoint, message);
            span.Dispose();
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
            TelemetrySpan span = Telemetry.CreateMessageSpan("OrderConsumer", endpoint, message);
            span.Dispose();
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
            TelemetrySpan span = Telemetry.CreateMessageSpan("OrderConsumer", endpoint, message);
            span.Dispose();
        });
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

