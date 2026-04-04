using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions.Consumer;
using BusWorks.Abstractions.Events;
using BusWorks.BackgroundServices;
using Shouldly;
using Xunit;

namespace BusWorks.Tests.UnitTests.Consumers;

/// <summary>
/// Verifies that <c>ToMessageContext()</c> inside <c>BuildTypedProcessor&lt;T&gt;</c>
/// correctly maps every property from <see cref="ServiceBusReceivedMessage"/>
/// to <see cref="MessageContext"/> before calling <see cref="IConsumer{T}.Consume"/>.
/// </summary>
public sealed class MessageContextMappingTests
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

    [Fact]
    public async Task MessageId_IsMappedFromBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(messageId: "test-message-id"), TestContext.Current.CancellationToken);

        consumer.CapturedMetadata!.MessageId.ShouldBe("test-message-id");
    }

    [Fact]
    public async Task SessionId_IsMappedFromBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(sessionId: "session-42"), TestContext.Current.CancellationToken);

        consumer.CapturedMetadata!.SessionId.ShouldBe("session-42");
    }

    [Fact]
    public async Task SessionId_IsNull_WhenNotSetOnBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(), TestContext.Current.CancellationToken);

        consumer.CapturedMetadata!.SessionId.ShouldBeNull();
    }

    [Fact]
    public async Task CorrelationId_IsMappedFromBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(correlationId: "corr-999"), TestContext.Current.CancellationToken);

        consumer.CapturedMetadata!.CorrelationId.ShouldBe("corr-999");
    }

    [Fact]
    public async Task DeliveryCount_IsMappedFromBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(deliveryCount: 3), TestContext.Current.CancellationToken);

        consumer.CapturedMetadata!.DeliveryCount.ShouldBe(3);
    }

    [Fact]
    public async Task SequenceNumber_IsMappedFromBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(sequenceNumber: 1_000_001), TestContext.Current.CancellationToken);

        consumer.CapturedMetadata!.SequenceNumber.ShouldBe(1_000_001);
    }

    [Fact]
    public async Task EnqueuedTime_IsMappedFromBrokerMessage()
    {
        var enqueued = new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero);
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(enqueuedTime: enqueued), TestContext.Current.CancellationToken);

        consumer.CapturedMetadata!.EnqueuedTime.ShouldBe(enqueued);
    }

    [Fact]
    public async Task ContentType_IsMappedFromBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(contentType: "application/json"), TestContext.Current.CancellationToken);

        consumer.CapturedMetadata!.ContentType.ShouldBe("application/json");
    }

    [Fact]
    public async Task Subject_IsMappedFromBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(subject: "parking.reservation.created"), TestContext.Current.CancellationToken);

        consumer.CapturedMetadata!.Subject.ShouldBe("parking.reservation.created");
    }

    [Fact]
    public async Task ApplicationProperties_AreMappedFromBrokerMessage()
    {
        var props = new Dictionary<string, object>
        {
            ["EventType"]   = "ParkingReservationCreated",
            ["traceparent"] = "00-abc123-def456-01"
        };
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(applicationProperties: props), TestContext.Current.CancellationToken);

        consumer.CapturedMetadata!.ApplicationProperties["EventType"].ShouldBe("ParkingReservationCreated");
        consumer.CapturedMetadata.ApplicationProperties["traceparent"].ShouldBe("00-abc123-def456-01");
    }

    [Fact]
    public async Task ApplicationProperties_IsEmpty_NotNull_WhenNoneSetOnBrokerMessage()
    {
        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(), TestContext.Current.CancellationToken);

        consumer.CapturedMetadata!.ApplicationProperties.ShouldNotBeNull();
        consumer.CapturedMetadata.ApplicationProperties.ShouldBeEmpty();
    }

    [Fact]
    public async Task AllMetadataProperties_AreMappedInOneMessage()
    {
        var enqueued = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
        var props = new Dictionary<string, object> { ["k"] = "v" };

        var consumer = new MetadataCapturingConsumer();
        await InvokeAsync(consumer, CreateMessage(
            messageId:             "full-msg",
            sessionId:             "full-session",
            correlationId:         "full-corr",
            deliveryCount:         2,
            sequenceNumber:        42L,
            enqueuedTime:          enqueued,
            contentType:           "application/json",
            subject:               "full-subject",
            applicationProperties: props), TestContext.Current.CancellationToken);

        MessageContext md = consumer.CapturedMetadata!;
        md.MessageId.ShouldBe("full-msg");
        md.SessionId.ShouldBe("full-session");
        md.CorrelationId.ShouldBe("full-corr");
        md.DeliveryCount.ShouldBe(2);
        md.SequenceNumber.ShouldBe(42L);
        md.EnqueuedTime.ShouldBe(enqueued);
        md.ContentType.ShouldBe("application/json");
        md.Subject.ShouldBe("full-subject");
        md.ApplicationProperties["k"].ShouldBe("v");
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

