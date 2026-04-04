using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions.Events;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;
using Xunit;

namespace BusWorks.Tests.IntegrationTests.Consumers;

/// <summary>
/// Abstract base class for consumer integration tests that captures all behaviour shared
/// between queue and topic scenarios via the Template Method pattern.
/// <para>
/// Concrete subclasses are responsible for:
/// <list type="bullet">
///   <item>Provisioning the broker entities (<see cref="TestBase.ProvisionEntitiesAsync"/>).</item>
///   <item>Supplying a fresh test event (<see cref="NewEvent"/>).</item>
///   <item>Returning correctly-configured <see cref="ServiceBusReceiver"/> instances.</item>
///   <item>Implementing the publish-and-consume round-trip (<see cref="PublishAndConsumeAsync"/>).</item>
///   <item>Asserting the deserialized event's domain properties (<see cref="AssertDeserializedEventAsync"/>).</item>
///   <item>Optionally draining stale messages before a test (<see cref="DrainAsync"/>).</item>
/// </list>
/// </para>
/// </summary>
public abstract class ConsumerTestBase<TEvent> : TestBase
    where TEvent : IIntegrationEvent
{
    protected readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(15);
    protected static int MaxDeliveryCount => 3;

    protected ConsumerTestBase(EventBusHostFactory factory) : base(factory) { }

    /// <summary>Creates a new domain event instance with realistic test data.</summary>
    protected abstract TEvent NewEvent();

    /// <summary>
    /// Publishes <paramref name="event"/> and returns the raw broker message,
    /// consuming it with <see cref="ServiceBusReceiveMode.ReceiveAndDelete"/>.
    /// Implementations are responsible for draining stale messages before opening
    /// the receiver so that the message returned belongs to this specific invocation.
    /// </summary>
    protected abstract Task<ServiceBusReceivedMessage> PublishAndReceiveRawAsync(
        TEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the event and runs the resulting broker message through the real consumer
    /// deserialization pipeline, returning the fully-typed domain event exactly as a production
    /// consumer would receive it.
    /// </summary>
    protected abstract Task<TEvent> PublishAndConsumeAsync(
        TEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a <see cref="ServiceBusReceiveMode.ReceiveAndDelete"/> receiver
    /// targeting the primary test entity (queue or subscription).
    /// </summary>
    protected abstract ServiceBusReceiver CreateDeleteReceiver();

    /// <summary>
    /// Creates a <see cref="ServiceBusReceiveMode.PeekLock"/> receiver
    /// targeting the primary test entity (queue or subscription).
    /// </summary>
    protected abstract ServiceBusReceiver CreatePeekLockReceiver();

    /// <summary>
    /// Creates a <see cref="ServiceBusReceiveMode.ReceiveAndDelete"/> receiver
    /// targeting the dead-letter sub-queue of the primary test entity.
    /// </summary>
    protected abstract ServiceBusReceiver CreateDeadLetterReceiver();

    /// <summary>
    /// Asserts that each domain-specific property of <paramref name="received"/> matches
    /// the corresponding property on the originally-published <paramref name="expected"/> event.
    /// Implemented by each concrete class because the properties differ per event type.
    /// </summary>
    protected abstract Task AssertDeserializedEventAsync(TEvent expected, TEvent received);

    /// <summary>
    /// Removes stale messages from all relevant broker entities before a test.
    /// Queue tests leave this as a no-op; topic tests override to drain every subscription.
    /// </summary>
    protected virtual Task DrainAsync() => Task.CompletedTask;


    [Fact]
    public async Task PublishedMessage_IsDelivered()
    {
        TEvent @event = NewEvent();

        ServiceBusReceivedMessage raw = await PublishAndReceiveRawAsync(@event, TestContext.Current.CancellationToken);

        Assert.NotNull(raw);
    }

    [Fact]
    public async Task PublishMultipleMessages_AllAreDelivered()
    {
        const int messageCount = 5;
        var published = Enumerable.Range(0, messageCount).Select(_ => NewEvent()).ToList();

        await DrainAsync();

        await using ServiceBusReceiver receiver = CreateDeleteReceiver();

        foreach (TEvent e in published)
            await PublishAsync(e, TestContext.Current.CancellationToken);

        List<ServiceBusReceivedMessage> received = [];
        while (received.Count < messageCount)
        {
            IReadOnlyList<ServiceBusReceivedMessage> batch =
                await receiver.ReceiveMessagesAsync(messageCount - received.Count, ReceiveTimeout, TestContext.Current.CancellationToken);

            if (batch.Count == 0)
                break;

            received.AddRange(batch);
        }

        Assert.Equal(messageCount, received.Count);

        var receivedIds = received.Select(m => Guid.Parse(m.MessageId)).ToHashSet();

        foreach (TEvent e in published)
            Assert.Contains(e.Id, receivedIds);
    }

    [Fact]
    public async Task PublishedMessage_ContentType_IsApplicationJson()
    {
        TEvent @event = NewEvent();

        ServiceBusReceivedMessage raw = await PublishAndReceiveRawAsync(@event, TestContext.Current.CancellationToken);

        Assert.Equal("application/json", raw.ContentType);
    }

    [Fact]
    public async Task PublishedMessage_MessageId_MatchesEventId()
    {
        TEvent @event = NewEvent();

        ServiceBusReceivedMessage raw = await PublishAndReceiveRawAsync(@event, TestContext.Current.CancellationToken);

        Assert.Equal(@event.Id.ToString(), raw.MessageId);
    }

    [Fact]
    public async Task PublishedMessage_CorrelationId_MatchesEventId()
    {
        TEvent @event = NewEvent();

        ServiceBusReceivedMessage raw = await PublishAndReceiveRawAsync(@event, TestContext.Current.CancellationToken);

        Assert.Equal(@event.Id.ToString(), raw.CorrelationId);
    }

    [Fact]
    public async Task Consumer_Deserializes_AllEventProperties_Correctly()
    {
        TEvent @event = NewEvent();

        TEvent received = await PublishAndConsumeAsync(@event, TestContext.Current.CancellationToken);

        await AssertDeserializedEventAsync(@event, received);
    }

    [Fact]
    public async Task AbandonedMessage_IsRequeued_WithIncrementedDeliveryCount()
    {
        await DrainAsync();

        await using ServiceBusReceiver receiver = CreatePeekLockReceiver();

        await PublishAsync(NewEvent(), TestContext.Current.CancellationToken);

        ServiceBusReceivedMessage? firstDelivery = await receiver.ReceiveMessageAsync(ReceiveTimeout, TestContext.Current.CancellationToken);
        Assert.NotNull(firstDelivery);

        // Simulate a transient failure — abandoning returns the message to the active entity.
        await receiver.AbandonMessageAsync(firstDelivery, cancellationToken: TestContext.Current.CancellationToken);

        ServiceBusReceivedMessage? retryDelivery = await receiver.ReceiveMessageAsync(ReceiveTimeout, TestContext.Current.CancellationToken);

        Assert.NotNull(retryDelivery);
        Assert.Equal(2, retryDelivery.DeliveryCount);
        Assert.Equal(firstDelivery.MessageId, retryDelivery.MessageId);

        // Cleanup — complete the retry delivery to leave the entity clean.
        await receiver.CompleteMessageAsync(retryDelivery, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Message_ExceedingMaxDeliveryCount_IsMovedToDeadLetterQueue()
    {
        TEvent @event = NewEvent();

        await DrainAsync();

        await using ServiceBusReceiver activeReceiver = CreatePeekLockReceiver();

        await PublishAsync(@event, TestContext.Current.CancellationToken);

        // Exhaust the delivery budget; the final abandon triggers the broker to DLQ the message.
        for (int delivery = 1; delivery <= MaxDeliveryCount; delivery++)
        {
            ServiceBusReceivedMessage? message = await activeReceiver.ReceiveMessageAsync(ReceiveTimeout, TestContext.Current.CancellationToken);

            Assert.NotNull(message);
            Assert.Equal(delivery, message.DeliveryCount);

            await activeReceiver.AbandonMessageAsync(message, cancellationToken: TestContext.Current.CancellationToken);
        }

        // Active entity should now be empty.
        ServiceBusReceivedMessage? shouldBeNull =
            await activeReceiver.ReceiveMessageAsync(maxWaitTime: TimeSpan.FromSeconds(3), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(shouldBeNull);

        // The message should be present in the dead-letter sub-queue.
        // The DLQ sub-queue is provisioned automatically by the broker alongside the parent entity.
        await using ServiceBusReceiver dlqReceiver = CreateDeadLetterReceiver();

        ServiceBusReceivedMessage? deadLettered = await dlqReceiver.ReceiveMessageAsync(ReceiveTimeout, TestContext.Current.CancellationToken);

        Assert.NotNull(deadLettered);
        Assert.Equal(@event.Id.ToString(), deadLettered.MessageId);
        Assert.Equal(MaxDeliveryCount + 1, deadLettered.DeliveryCount);
    }
}
