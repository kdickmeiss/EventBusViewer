using System.Reflection;
using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Consumer;
using BusWorks.Abstractions.Events;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;
using Shouldly;
using Xunit;

namespace BusWorks.Tests.IntegrationTests.Consumers;

/// <summary>
/// Abstract base class for consumer integration tests that drives the real
/// <c>ServiceBusProcessorBackgroundService</c> pipeline end-to-end.
/// <para>
/// Concrete subclasses are responsible for:
/// <list type="bullet">
///   <item>Provisioning any extra broker entities (override <see cref="TestBase.InitializeAsync"/>).</item>
///   <item>Supplying a fresh test event (<see cref="NewEvent"/>).</item>
///   <item>Identifying the consumer type to verify (<see cref="ConsumerType"/>).</item>
///   <item>Creating a dead-letter receiver for the primary entity (<see cref="CreateDeadLetterReceiver"/>).</item>
///   <item>Asserting domain-specific event properties (<see cref="AssertDeserializedEvent"/>).</item>
///   <item>Optionally draining stale state before a test (<see cref="DrainAsync"/>).</item>
/// </list>
/// </para>
/// </summary>
public abstract class ConsumerTestBase<TEvent> : TestBase
    where TEvent : IIntegrationEvent
{
    /// <summary>
    /// How long to wait for the background service to deliver and process a message.
    /// </summary>
    protected readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Must match the <c>MaxDeliveryCount</c> set on the test consumer's
    /// <c>[ServiceBusQueue]</c> / <c>[ServiceBusTopic]</c> attribute so that the
    /// <see cref="Consumer_ExceedingMaxDeliveryCount_IsMovedToDeadLetterQueue"/> test
    /// primes exactly the right number of failures.
    /// Reads directly from the attribute so it stays in sync automatically.
    /// </summary>
    protected int MaxDeliveryCount =>
        ConsumerType.GetCustomAttribute<ServiceBusQueueAttribute>()?.MaxDeliveryCount
        ?? ConsumerType.GetCustomAttribute<ServiceBusTopicAttribute>()?.MaxDeliveryCount
        ?? 5;

    protected ConsumerTestBase(EventBusHostFactory factory) : base(factory) { }

    /// <summary>
    /// Clears any stale capture-channel entries and resets the <see cref="TestConsumerCapture{TEvent}"/>
    /// fail counter so every test starts with a pristine capture state.
    /// </summary>
    public override ValueTask InitializeAsync()
    {
        GetRequiredService<TestConsumerCapture<TEvent>>().Drain();
        return base.InitializeAsync();
    }

    // ── Abstract members ───────────────────────────────────────────────────

    /// <summary>Creates a new domain event instance with realistic test data.</summary>
    protected abstract TEvent NewEvent();

    /// <summary>
    /// The concrete consumer type registered in DI that handles <typeparamref name="TEvent"/>.
    /// Used by <see cref="PublishAndConsumeAsync"/> to identify which capture to read from.
    /// </summary>
    protected abstract Type ConsumerType { get; }

    /// <summary>
    /// Creates a <see cref="ServiceBusReceiveMode.ReceiveAndDelete"/> receiver targeting the
    /// dead-letter sub-queue of the primary test entity (queue or subscription).
    /// Used by <see cref="Consumer_ExceedingMaxDeliveryCount_IsMovedToDeadLetterQueue"/> and
    /// the DLQ drain inside <see cref="DrainAsync"/>.
    /// </summary>
    protected abstract ServiceBusReceiver CreateDeadLetterReceiver();

    /// <summary>
    /// Asserts that each domain-specific property of <paramref name="received"/> matches
    /// <paramref name="expected"/>. Implemented per subclass because the properties differ
    /// per event type.
    /// </summary>
    protected abstract void AssertDeserializedEvent(TEvent expected, TEvent received);

    // ── Virtual members ────────────────────────────────────────────────────

    /// <summary>
    /// Publishes <paramref name="event"/>, waits for the
    /// <c>ServiceBusProcessorBackgroundService</c> to deserialize and dispatch it via
    /// the DI-registered consumer, and returns the captured event together with the
    /// broker metadata the consumer received.
    /// <para>
    /// Any messages captured before <paramref name="event"/> is seen (stale deliveries
    /// from a preceding test that arrived after the per-test <see cref="InitializeAsync"/>
    /// drain) are silently discarded. The overall wait is bounded by <see cref="ReceiveTimeout"/>.
    /// </para>
    /// </summary>
    protected virtual async Task<(TEvent Event, MessageContext Metadata)> PublishAndConsumeAsync(
        TEvent @event,
        CancellationToken cancellationToken = default)
    {
        await PublishAsync(@event, cancellationToken);

        TestConsumerCapture<TEvent> capture = GetRequiredService<TestConsumerCapture<TEvent>>();

        // A single deadline CTS bounds the total wait across all channel reads; stale
        // messages with a different Id are skipped rather than accepted as the result.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ReceiveTimeout);

        try
        {
            while (true)
            {
                (TEvent received, MessageContext ctx) =
                    await capture.ReadAsync(ReceiveTimeout, deadline.Token);

                if (received.Id == @event.Id)
                    return (received, ctx);
            }
        }
        catch (OperationCanceledException)
            when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Event '{typeof(TEvent).Name}' with Id '{@event.Id}' was not captured within " +
                $"{ReceiveTimeout.TotalSeconds} s. " +
                "Verify the consumer is registered in DI and the background service is running.");
        }
    }

    /// <summary>
    /// Resets the capture channel and dead-letter queue so the next test starts clean.
    /// Topic subclasses override to additionally drain their fan-out subscriptions.
    /// </summary>
    protected virtual async Task DrainAsync()
    {
        // Clear the dead-letter sub-queue so the DLQ assertion test starts with a clean slate.
        await using ServiceBusReceiver dlqReceiver = CreateDeadLetterReceiver();
        IReadOnlyList<ServiceBusReceivedMessage> batch;
        do
        {
            batch = await dlqReceiver.ReceiveMessagesAsync(
                maxMessages: 100, maxWaitTime: TimeSpan.FromMilliseconds(300));
        }
        while (batch.Count > 0);
    }

    // ── Shared tests ───────────────────────────────────────────────────────

    [Fact]
    public async Task PublishMultipleMessages_AllAreDelivered()
    {
        const int messageCount = 5;
        var published = Enumerable.Range(0, messageCount).Select(_ => NewEvent()).ToList();
        var expectedIds = published.Select(e => e.Id).ToHashSet();

        await DrainAsync();

        TestConsumerCapture<TEvent> capture = GetRequiredService<TestConsumerCapture<TEvent>>();

        foreach (TEvent e in published)
            await PublishAsync(e, TestContext.Current.CancellationToken);

        // Read until all expected IDs have been captured; skip any stale messages that
        // the background service may have processed from a previous (failed) test run.
        var receivedIds = new HashSet<Guid>();
        while (receivedIds.Count < messageCount)
        {
            (TEvent received, _) = await capture.ReadAsync(ReceiveTimeout, TestContext.Current.CancellationToken);
            if (expectedIds.Contains(received.Id))
                receivedIds.Add(received.Id);
        }

        receivedIds.SetEquals(expectedIds).ShouldBeTrue();
    }

    [Fact]
    public async Task PublishedMessage_ContentType_IsApplicationJson()
    {
        (_, MessageContext ctx) = await PublishAndConsumeAsync(
            NewEvent(), TestContext.Current.CancellationToken);

        ctx.ContentType.ShouldBe("application/json");
    }

    [Fact]
    public async Task PublishedMessage_MessageId_MatchesEventId()
    {
        TEvent @event = NewEvent();
        (_, MessageContext ctx) = await PublishAndConsumeAsync(@event, TestContext.Current.CancellationToken);

        ctx.MessageId.ShouldBe(@event.Id.ToString());
    }

    [Fact]
    public async Task PublishedMessage_CorrelationId_MatchesEventId()
    {
        TEvent @event = NewEvent();
        (_, MessageContext ctx) = await PublishAndConsumeAsync(@event, TestContext.Current.CancellationToken);

        ctx.CorrelationId.ShouldBe(@event.Id.ToString());
    }

    [Fact]
    public async Task Consumer_Deserializes_AllEventProperties_Correctly()
    {
        TEvent @event = NewEvent();
        (TEvent received, _) = await PublishAndConsumeAsync(@event, TestContext.Current.CancellationToken);

        AssertDeserializedEvent(@event, received);
    }

    [Fact]
    public async Task Consumer_AbandonedMessage_IsRetried_WithIncrementedDeliveryCount()
    {
        await DrainAsync();

        TEvent @event = NewEvent();
        TestConsumerCapture<TEvent> capture = GetRequiredService<TestConsumerCapture<TEvent>>();

        // Prime the consumer to throw once — the background service will call AbandonMessageAsync,
        // the broker will requeue the message, and the second delivery should succeed.
        capture.FailNextN(1);
        await PublishAsync(@event, TestContext.Current.CancellationToken);

        (TEvent received, MessageContext ctx) =
            await capture.ReadAsync(ReceiveTimeout, TestContext.Current.CancellationToken);

        received.Id.ShouldBe(@event.Id);
        ctx.DeliveryCount.ShouldBe(2);
    }

    [Fact]
    public async Task Consumer_ExceedingMaxDeliveryCount_IsMovedToDeadLetterQueue()
    {
        await DrainAsync();

        TEvent @event = NewEvent();
        TestConsumerCapture<TEvent> capture = GetRequiredService<TestConsumerCapture<TEvent>>();

        // Fail on every delivery up to the application-level limit. The background service
        // calls DeadLetterMessageAsync on the final attempt (DeliveryCount == MaxDeliveryCount).
        capture.FailNextN(MaxDeliveryCount);
        await PublishAsync(@event, TestContext.Current.CancellationToken);

        // All retries may take several seconds — use a generous timeout.
        await using ServiceBusReceiver dlqReceiver = CreateDeadLetterReceiver();
        ServiceBusReceivedMessage? deadLettered = await dlqReceiver.ReceiveMessageAsync(
            TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        deadLettered.ShouldNotBeNull();
        deadLettered.MessageId.ShouldBe(@event.Id.ToString());
    }
}
