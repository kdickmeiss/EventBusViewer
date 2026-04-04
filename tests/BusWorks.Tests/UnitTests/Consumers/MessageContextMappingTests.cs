using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions;
using BusWorks.Abstractions.Consumer;
using BusWorks.Abstractions.Events;
using BusWorks.BackgroundServices;

namespace BusWorks.Tests.UnitTests.Consumers;

/// <summary>
/// Verifies that <c>ToMessageContext()</c> inside <c>BuildTypedProcessor&lt;T&gt;</c>
/// correctly maps every property from <see cref="ServiceBusReceivedMessage"/>
/// to <see cref="MessageContext"/> before calling <see cref="IConsumer{T}.Consume"/>.
/// </summary>
internal sealed class MessageContextMappingTests
{
    // A minimal event type — the content does not matter for metadata mapping tests.
    private sealed record MetadataEvent(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;

    // Captures only the MessageContext so assertions stay focused.
    private sealed class MetadataCapturingConsumer : IConsumer<MetadataEvent>
    {
        public MessageContext? CapturedMetadata { get; private set; }

        public Task Consume(IConsumeContext<MetadataEvent> context)
        {
            CapturedMetadata = context.Metadata;
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task MessageId_IsMappedFromBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(messageId: "test-message-id"));

        await Assert.That(consumer.CapturedMetadata!.MessageId).IsEqualTo("test-message-id");
    }

    [Test]
    public async Task SessionId_IsMappedFromBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(sessionId: "session-42"));

        await Assert.That(consumer.CapturedMetadata!.SessionId).IsEqualTo("session-42");
    }

    [Test]
    public async Task SessionId_IsNull_WhenNotSetOnBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage());

        await Assert.That(consumer.CapturedMetadata!.SessionId).IsNull();
    }

    [Test]
    public async Task CorrelationId_IsMappedFromBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(correlationId: "corr-999"));

        await Assert.That(consumer.CapturedMetadata!.CorrelationId).IsEqualTo("corr-999");
    }

    [Test]
    public async Task DeliveryCount_IsMappedFromBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(deliveryCount: 3));

        await Assert.That(consumer.CapturedMetadata!.DeliveryCount).IsEqualTo(3);
    }

    [Test]
    public async Task SequenceNumber_IsMappedFromBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(sequenceNumber: 1_000_001));

        await Assert.That(consumer.CapturedMetadata!.SequenceNumber).IsEqualTo(1_000_001);
    }

    [Test]
    public async Task EnqueuedTime_IsMappedFromBrokerMessage()
    {
        var enqueued = new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero);
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(enqueuedTime: enqueued));

        await Assert.That(consumer.CapturedMetadata!.EnqueuedTime).IsEqualTo(enqueued);
    }

    [Test]
    public async Task ContentType_IsMappedFromBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(contentType: "application/json"));

        await Assert.That(consumer.CapturedMetadata!.ContentType).IsEqualTo("application/json");
    }

    [Test]
    public async Task Subject_IsMappedFromBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(subject: "parking.reservation.created"));

        await Assert.That(consumer.CapturedMetadata!.Subject).IsEqualTo("parking.reservation.created");
    }

    [Test]
    public async Task ApplicationProperties_AreMappedFromBrokerMessage()
    {
        var props = new Dictionary<string, object>
        {
            ["EventType"]   = "ParkingReservationCreated",
            ["traceparent"] = "00-abc123-def456-01"
        };
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(applicationProperties: props));

        await Assert.That(consumer.CapturedMetadata!.ApplicationProperties["EventType"])
            .IsEqualTo("ParkingReservationCreated");
        await Assert.That(consumer.CapturedMetadata.ApplicationProperties["traceparent"])
            .IsEqualTo("00-abc123-def456-01");
    }

    [Test]
    public async Task ApplicationProperties_IsEmpty_NotNull_WhenNoneSetOnBrokerMessage()
    {
        // Consumers that access ApplicationProperties must never see null —
        // MessageContext guarantees an empty dictionary when the sender set nothing.
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage());

        await Assert.That(consumer.CapturedMetadata!.ApplicationProperties).IsNotNull();
        await Assert.That(consumer.CapturedMetadata.ApplicationProperties.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AllMetadataProperties_AreMappedInOneMessage()
    {
        // Smoke test: a single message with every property set — verifies nothing is
        // accidentally overwritten when all fields are populated simultaneously.
        var enqueued = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
        var props = new Dictionary<string, object> { ["k"] = "v" };

        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(
            messageId:           "full-msg",
            sessionId:           "full-session",
            correlationId:       "full-corr",
            deliveryCount:       2,
            sequenceNumber:      42L,
            enqueuedTime:        enqueued,
            contentType:         "application/json",
            subject:             "full-subject",
            applicationProperties: props));

        MessageContext md = consumer.CapturedMetadata!;
        await Assert.That(md.MessageId).IsEqualTo("full-msg");
        await Assert.That(md.SessionId).IsEqualTo("full-session");
        await Assert.That(md.CorrelationId).IsEqualTo("full-corr");
        await Assert.That(md.DeliveryCount).IsEqualTo(2);
        await Assert.That(md.SequenceNumber).IsEqualTo(42L);
        await Assert.That(md.EnqueuedTime).IsEqualTo(enqueued);
        await Assert.That(md.ContentType).IsEqualTo("application/json");
        await Assert.That(md.Subject).IsEqualTo("full-subject");
        await Assert.That(md.ApplicationProperties["k"]).IsEqualTo("v");
    }

    private static ServiceBusReceivedMessage CreateMessage(
        string messageId = "msg-1",
        string? sessionId = null,
        string? correlationId = null,
        int deliveryCount = 1,
        long sequenceNumber = 1,
        DateTimeOffset? enqueuedTime = null,
        string? contentType = null,
        string? subject = null,
        IDictionary<string, object>? applicationProperties = null)
    {
        var body = BinaryData.FromObjectAsJson(
            new MetadataEvent(Guid.NewGuid(), DateTime.UtcNow));

        return ServiceBusModelFactory.ServiceBusReceivedMessage(
            body:                  body,
            messageId:             messageId,
            sessionId:             sessionId,
            correlationId:         correlationId,
            deliveryCount:         deliveryCount,
            sequenceNumber:        sequenceNumber,
            enqueuedTime:          enqueuedTime ?? default,
            contentType:           contentType,
            subject:               subject,
            properties:            applicationProperties);
    }

    private static Task InvokeAsync<T>(
        IConsumer<T> consumer,
        ServiceBusReceivedMessage message,
        CancellationToken ct = default) where T : class, IIntegrationEvent
    {
        Func<ServiceBusReceivedMessage, CancellationToken, Task> processor = ServiceBusMessageProcessorBuilder.BuildTypedProcessor(consumer);
        return processor(message, ct);
    }
}

