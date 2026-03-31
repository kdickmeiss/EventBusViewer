using System.Reflection;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BusWorks.BackgroundServices;
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
        JsonSerializerOptions first  = ServiceBusConsumerDefaults.JsonSerializerOptions;
        JsonSerializerOptions second = ServiceBusConsumerDefaults.JsonSerializerOptions;

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task ValidBody_DeserializesMessage_AndCallsConsume()
    {
        var id         = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var occurredOn = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var body       = BinaryData.FromObjectAsJson(new TestEvent(id, occurredOn, "park-opened", 42));
        ServiceBusReceivedMessage raw = CreateMessage(body);
        var consumer = new TrackingConsumer();

        await InvokeAsync(consumer, raw);

        await Assert.That(consumer.ReceivedMessage).IsNotNull();
        await Assert.That(consumer.ReceivedMessage!.Id).IsEqualTo(id);
        await Assert.That(consumer.ReceivedMessage.Name).IsEqualTo("park-opened");
        await Assert.That(consumer.ReceivedMessage.Value).IsEqualTo(42);
    }

    [Test]
    public async Task ValidBody_ForwardsMetadata_AndCancellationToken()
    {
        // The framework maps ServiceBusReceivedMessage → MessageContext before calling Consume.
        // Verify the mapping is applied and the token is forwarded unchanged.
        var body = BinaryData.FromObjectAsJson(new TestEvent(Guid.NewGuid(), DateTime.UtcNow, "test", 1));
        ServiceBusReceivedMessage raw = CreateMessage(body, messageId: "msg-forward");
        var consumer = new TrackingConsumer();
        using var cts = new CancellationTokenSource();

        await InvokeAsync(consumer, raw, cts.Token);

        await Assert.That(consumer.ReceivedMetadata).IsNotNull();
        await Assert.That(consumer.ReceivedMetadata!.MessageId).IsEqualTo("msg-forward");
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
        ServiceBusReceivedMessage raw = CreateMessage(BinaryData.FromString(json));
        var consumer = new TrackingConsumer();

        await InvokeAsync(consumer, raw);

        await Assert.That(consumer.ReceivedMessage!.Name).IsEqualTo("park-opened");
        await Assert.That(consumer.ReceivedMessage.Value).IsEqualTo(99);
    }

    [Test]
    public async Task NullDeserializationResult_ThrowsInvalidOperationException_WithMessageIdAndTypeName()
    {
        // The JSON literal "null" deserializes reference types to null — must be rejected.
        ServiceBusReceivedMessage raw = CreateMessage(BinaryData.FromString("null"), messageId: "msg-null");
        var consumer = new TrackingConsumer();

        InvalidOperationException? exception = null;
        try { await InvokeAsync(consumer, raw); }
        catch (InvalidOperationException ex) { exception = ex; }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message.Contains("msg-null")).IsTrue();
        await Assert.That(exception.Message.Contains(nameof(TestEvent))).IsTrue();
    }

    [Test]
    public async Task InvalidJson_PropagatesJsonException()
    {
        ServiceBusReceivedMessage raw = CreateMessage(BinaryData.FromString("{not valid json}"));
        var consumer = new TrackingConsumer();

        JsonException? exception = null;
        try { await InvokeAsync(consumer, raw); }
        catch (JsonException ex) { exception = ex; }

        await Assert.That(exception).IsNotNull();
    }

    [Test]
    public async Task ExceptionInConsume_PropagatesOut()
    {
        var body = BinaryData.FromObjectAsJson(new TestEvent(Guid.NewGuid(), DateTime.UtcNow, "test", 1));
        ServiceBusReceivedMessage raw = CreateMessage(body);
        var consumer = new FaultingConsumer();

        InvalidOperationException? exception = null;
        try { await InvokeAsync(consumer, raw); }
        catch (InvalidOperationException ex) { exception = ex; }

        await Assert.That(exception).IsEqualTo(FaultingConsumer.Error);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ServiceBusReceivedMessage CreateMessage(BinaryData body, string messageId = "msg-1") =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(body: body, messageId: messageId);

    /// <summary>
    /// Exercises the full framework deserialization and dispatch pipeline by reflecting
    /// into <c>BuildTypedProcessor&lt;T&gt;</c> — the private static method that wires
    /// JSON deserialization, <see cref="MessageContext"/> mapping, and <see cref="IConsumer{T}.Consume"/>
    /// into a single <c>Func&lt;ServiceBusReceivedMessage, CancellationToken, Task&gt;</c>.
    /// </summary>
    // S3011 — test-only reflection into a private static pure function; no security risk.
#pragma warning disable S3011
    private static Task InvokeAsync<T>(
        IConsumer<T> consumer,
        ServiceBusReceivedMessage message,
        CancellationToken ct = default) where T : class, IIntegrationEvent
    {
        var processor = (Func<ServiceBusReceivedMessage, CancellationToken, Task>)
            typeof(ServiceBusProcessorBackgroundService)
                .GetMethod("BuildTypedProcessor", BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(typeof(T))
                .Invoke(null, [consumer])!;

        return processor(message, ct);
    }
#pragma warning restore S3011
}
