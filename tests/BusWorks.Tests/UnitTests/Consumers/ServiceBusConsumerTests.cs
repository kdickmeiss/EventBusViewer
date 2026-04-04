using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions;
using BusWorks.Abstractions.Consumer;
using BusWorks.Abstractions.Events;
using BusWorks.BackgroundServices;
using BusWorks.Consumer;
using Xunit;

namespace BusWorks.Tests.UnitTests.Consumers;

public sealed partial class ServiceBusConsumerTests
{
    [Fact]
    public void SharedJsonSerializerOptions_PropertyNameCaseInsensitive_IsTrue()
    {
        bool isCaseInsensitive = ServiceBusConsumerDefaults.JsonSerializerOptions.PropertyNameCaseInsensitive;

        Assert.True(isCaseInsensitive);
    }

    [Fact]
    public void SharedJsonSerializerOptions_ReturnsSameInstanceOnEachAccess()
    {
        // The instance must be cached — creating a new JsonSerializerOptions on every
        // access would discard JsonSerializer's internal reflection cache and regress performance.
        JsonSerializerOptions first = ServiceBusConsumerDefaults.JsonSerializerOptions;
        JsonSerializerOptions second = ServiceBusConsumerDefaults.JsonSerializerOptions;

        Assert.Same(first, second);
    }

    [Fact]
    public async Task ValidBody_DeserializesMessage_AndCallsConsume()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var occurredOn = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var body = BinaryData.FromObjectAsJson(new TestEvent(id, occurredOn, "park-opened", 42));
        ServiceBusReceivedMessage raw = CreateMessage(body);
        var consumer = new TrackingConsumer();

        await InvokeAsync(consumer, raw, TestContext.Current.CancellationToken);

        Assert.NotNull(consumer.ReceivedMessage);
        Assert.Equal(id, consumer.ReceivedMessage!.Id);
        Assert.Equal("park-opened", consumer.ReceivedMessage.Name);
        Assert.Equal(42, consumer.ReceivedMessage.Value);
    }

    [Fact]
    public async Task ValidBody_ForwardsMetadata_AndCancellationToken()
    {
        var body = BinaryData.FromObjectAsJson(new TestEvent(Guid.NewGuid(), DateTime.UtcNow, "test", 1));
        ServiceBusReceivedMessage raw = CreateMessage(body, messageId: "msg-forward");
        var consumer = new TrackingConsumer();
        using var cts = new CancellationTokenSource();

        await InvokeAsync(consumer, raw, cts.Token);

        Assert.NotNull(consumer.ReceivedMetadata);
        Assert.Equal("msg-forward", consumer.ReceivedMetadata!.MessageId);
        Assert.Equal(cts.Token, consumer.ReceivedCancellationToken);
    }

    [Fact]
    public async Task CaseInsensitiveJson_IsDeserializedCorrectly()
    {
        const string json = """
                            {"id":"11111111-1111-1111-1111-111111111111","occurredonutc":"2026-01-01T12:00:00Z","name":"park-opened","value":99}
                            """;
        ServiceBusReceivedMessage raw = CreateMessage(BinaryData.FromString(json));
        var consumer = new TrackingConsumer();

        await InvokeAsync(consumer, raw, TestContext.Current.CancellationToken);

        Assert.Equal("park-opened", consumer.ReceivedMessage!.Name);
        Assert.Equal(99, consumer.ReceivedMessage.Value);
    }

    [Fact]
    public async Task NullDeserializationResult_ThrowsInvalidOperationException_WithMessageIdAndTypeName()
    {
        ServiceBusReceivedMessage raw = CreateMessage(BinaryData.FromString("null"), messageId: "msg-null");
        var consumer = new TrackingConsumer();

        InvalidOperationException? exception = null;
        try { await InvokeAsync(consumer, raw, TestContext.Current.CancellationToken); }
        catch (InvalidOperationException ex) { exception = ex; }

        Assert.NotNull(exception);
        Assert.Contains("msg-null", exception.Message);
        Assert.Contains(nameof(TestEvent), exception.Message);
    }

    [Fact]
    public async Task InvalidJson_PropagatesJsonException()
    {
        ServiceBusReceivedMessage raw = CreateMessage(BinaryData.FromString("{not valid json}"));
        var consumer = new TrackingConsumer();

        JsonException? exception = null;
        try { await InvokeAsync(consumer, raw, TestContext.Current.CancellationToken); }
        catch (JsonException ex) { exception = ex; }

        Assert.NotNull(exception);
    }

    [Fact]
    public async Task ExceptionInConsume_PropagatesOut()
    {
        var body = BinaryData.FromObjectAsJson(new TestEvent(Guid.NewGuid(), DateTime.UtcNow, "test", 1));
        ServiceBusReceivedMessage raw = CreateMessage(body);
        var consumer = new FaultingConsumer();

        InvalidOperationException? exception = null;
        try { await InvokeAsync(consumer, raw, TestContext.Current.CancellationToken); }
        catch (InvalidOperationException ex) { exception = ex; }

        Assert.Equal(FaultingConsumer.Error, exception);
    }

    private static ServiceBusReceivedMessage CreateMessage(BinaryData body, string messageId = "msg-1") =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(body: body, messageId: messageId);

    /// <summary>
    /// Exercises the full framework deserialization and dispatch pipeline via
    /// <see cref="ServiceBusMessageProcessorBuilder.BuildTypedProcessor{TMessage}"/>,
    /// which wires JSON deserialization, <see cref="MessageContext"/> mapping, and
    /// <see cref="IConsumer{T}.Consume"/> into a single delegate.
    /// </summary>
    private static Task InvokeAsync<T>(
        IConsumer<T> consumer,
        ServiceBusReceivedMessage message,
        CancellationToken ct = default) where T : class, IIntegrationEvent
    {
        Func<ServiceBusReceivedMessage, CancellationToken, Task> processor =
            ServiceBusMessageProcessorBuilder.BuildTypedProcessor(consumer);
        return processor(message, ct);
    }
}
