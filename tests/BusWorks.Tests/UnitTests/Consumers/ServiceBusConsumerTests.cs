using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BusWorks.Consumer;

namespace BusWorks.Tests.UnitTests.Consumers;

internal sealed partial class ServiceBusConsumerTests
{
    [Test]
    public async Task SharedJsonSerializerOptions_PropertyNameCaseInsensitive_IsTrue()
    {
        bool isCaseInsensitive = ServiceBusConsumerDefaults.JsonSerializerOptions.PropertyNameCaseInsensitive;

        await Assert.That(isCaseInsensitive).IsTrue();
    }

    [Test]
    public async Task SharedJsonSerializerOptions_ReturnsSameInstanceOnEachAccess()
    {
        // The instance must be cached — creating a new JsonSerializerOptions on every
        // access would discard JsonSerializer's internal reflection cache and regress performance.
        JsonSerializerOptions first = ServiceBusConsumerDefaults.JsonSerializerOptions;
        JsonSerializerOptions second = ServiceBusConsumerDefaults.JsonSerializerOptions;

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task ValidBody_DeserializesMessage_AndCallsProcessMessageAsync()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var occurredOn = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var body = BinaryData.FromObjectAsJson(new TestEvent(id, occurredOn, "park-opened", 42));
        ServiceBusReceivedMessage rawMessage = CreateMessage(body);
        var consumer = new TrackingConsumer();

        await InvokeAsync(consumer, rawMessage);

        await Assert.That(consumer.ReceivedMessage).IsNotNull();
        await Assert.That(consumer.ReceivedMessage!.Id).IsEqualTo(id);
        await Assert.That(consumer.ReceivedMessage.Name).IsEqualTo("park-opened");
        await Assert.That(consumer.ReceivedMessage.Value).IsEqualTo(42);
    }

    [Test]
    public async Task ValidBody_ForwardsOriginalMessage_AndCancellationToken()
    {
        var body = BinaryData.FromObjectAsJson(new TestEvent(Guid.NewGuid(), DateTime.UtcNow, "test", 1));
        ServiceBusReceivedMessage rawMessage = CreateMessage(body, "msg-forward");
        var consumer = new TrackingConsumer();
        using var cts = new CancellationTokenSource();

        await InvokeAsync(consumer, rawMessage, cts.Token);

        await Assert.That(consumer.ReceivedRawMessage).IsEqualTo(rawMessage);
        await Assert.That(consumer.ReceivedCancellationToken).IsEqualTo(cts.Token);
    }

    [Test]
    public async Task CaseInsensitiveJson_IsDeserializedCorrectly()
    {
        // The default options have PropertyNameCaseInsensitive = true, so lowercase property
        // names in JSON must match PascalCase C# properties.
        const string json = """
            {"id":"11111111-1111-1111-1111-111111111111","occurredonutc":"2026-01-01T12:00:00Z","name":"park-opened","value":99}
            """;
        ServiceBusReceivedMessage rawMessage = CreateMessage(BinaryData.FromString(json));
        var consumer = new TrackingConsumer();

        await InvokeAsync(consumer, rawMessage);

        await Assert.That(consumer.ReceivedMessage!.Name).IsEqualTo("park-opened");
        await Assert.That(consumer.ReceivedMessage.Value).IsEqualTo(99);
    }

    [Test]
    public async Task NullDeserializationResult_ThrowsInvalidOperationException_WithMessageIdAndTypeName()
    {
        // The JSON literal "null" deserializes reference types to null, which must be rejected.
        ServiceBusReceivedMessage rawMessage = CreateMessage(BinaryData.FromString("null"), messageId: "msg-null");
        var consumer = new TrackingConsumer();

        InvalidOperationException? exception = null;
        try
        {
            await InvokeAsync(consumer, rawMessage);
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNotNull();
        bool containsMessageId = exception!.Message.Contains("msg-null");
        bool containsTypeName = exception.Message.Contains(nameof(TestEvent));
        await Assert.That(containsMessageId).IsTrue();
        await Assert.That(containsTypeName).IsTrue();
    }

    [Test]
    public async Task InvalidJson_PropagatesJsonException()
    {
        ServiceBusReceivedMessage rawMessage = CreateMessage(BinaryData.FromString("{not valid json}"));
        var consumer = new TrackingConsumer();

        JsonException? exception = null;
        try
        {
            await InvokeAsync(consumer, rawMessage);
        }
        catch (JsonException ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task DefaultJsonSerializerOptions_IsSharedStaticInstance()
    {
        // Verifies the base class returns the shared cached instance rather than allocating
        // a new JsonSerializerOptions per call, which would regress deserialization performance.
        var consumer = new TrackingConsumer();

        await Assert.That(ReferenceEquals(consumer.ExposedOptions, ServiceBusConsumerDefaults.JsonSerializerOptions)).IsTrue();
    }

    [Test]
    public async Task OverriddenJsonSerializerOptions_AreUsedForDeserialization()
    {
        // With PropertyNameCaseInsensitive = false, the lowercase "name" in JSON does not
        // match the PascalCase "Name" property — Name stays null, proving custom options are used.
        const string json = """
            {"Id":"11111111-1111-1111-1111-111111111111","OccurredOnUtc":"2026-01-01T00:00:00Z","name":"should-not-match","Value":1}
            """;
        ServiceBusReceivedMessage rawMessage = CreateMessage(BinaryData.FromString(json));
        var consumer = new CustomOptionsConsumer();

        await InvokeAsync(consumer, rawMessage);

        await Assert.That(consumer.ReceivedMessage!.Name).IsNull();
    }

    [Test]
    public async Task ExceptionInProcessMessageAsync_PropagatesOut()
    {
        var body = BinaryData.FromObjectAsJson(new TestEvent(Guid.NewGuid(), DateTime.UtcNow, "test", 1));
        ServiceBusReceivedMessage rawMessage = CreateMessage(body);
        var consumer = new FaultingConsumer();

        InvalidOperationException? exception = null;
        try
        {
            await InvokeAsync(consumer, rawMessage);
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsEqualTo(FaultingConsumer.Error);
    }

    [Test]
    public async Task RawConsumer_ForwardsMessage_AndCancellationToken()
    {
        ServiceBusReceivedMessage rawMessage = CreateMessage(BinaryData.FromString("raw payload"), "msg-raw");
        var consumer = new TrackingRawConsumer();
        using var cts = new CancellationTokenSource();

        await InvokeAsync(consumer, rawMessage, cts.Token);

        await Assert.That(consumer.ReceivedMessage).IsEqualTo(rawMessage);
        await Assert.That(consumer.ReceivedCancellationToken).IsEqualTo(cts.Token);
    }

    [Test]
    public async Task RawConsumer_ExceptionInProcessMessageAsync_PropagatesOut()
    {
        ServiceBusReceivedMessage rawMessage = CreateMessage(BinaryData.FromString("payload"), "msg-raw-err");
        var consumer = new FaultingRawConsumer();

        InvalidOperationException? exception = null;
        try
        {
            await InvokeAsync(consumer, rawMessage);
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        await Assert.That(exception).IsEqualTo(FaultingRawConsumer.Error);
    }
    
    private static ServiceBusReceivedMessage CreateMessage(BinaryData body, string messageId = "msg-1") =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(body: body, messageId: messageId);

    private static Task InvokeAsync(
        object consumer,
        ServiceBusReceivedMessage message,
        CancellationToken ct = default) =>
        ((IServiceBusConsumer)consumer).ProcessMessageInternalAsync(message, ct);
}
