using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions.Consumer;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;
using Shouldly;
using Xunit;

namespace BusWorks.Tests.IntegrationTests.Consumers;

/// <summary>
/// End-to-end integration tests for the session queue consumer pipeline.
/// Verifies that <c>ServiceBusProcessorBackgroundService</c> correctly uses a
/// <see cref="ServiceBusSessionProcessor"/> when <c>RequireSession = true</c> is set,
/// and that the session ID propagated by the publisher reaches the consumer.
/// </summary>
public sealed partial class SessionQueueConsumerTests(EventBusHostFactory factory)
    : ConsumerTestBase<SessionQueueConsumerTests.CustomerPaymentCreatedEvent>(factory)
{
    protected override CustomerPaymentCreatedEvent NewEvent() =>
        new(Guid.NewGuid(), DateTime.UtcNow, $"cust_{Guid.NewGuid():N}", 49.99m);

    protected override Type ConsumerType => typeof(CapturingSessionCustomerConsumer);

    protected override ServiceBusReceiver CreateDeadLetterReceiver() =>
        Emulator.Client.CreateReceiver(
            SessionQueueName,
            new ServiceBusReceiverOptions
            {
                SubQueue = SubQueue.DeadLetter,
                ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
            });

    protected override void AssertDeserializedEvent(
        CustomerPaymentCreatedEvent expected,
        CustomerPaymentCreatedEvent received)
    {
        received.Id.ShouldBe(expected.Id);
        received.OccurredOnUtc.ShouldBe(expected.OccurredOnUtc);
        received.CustomerId.ShouldBe(expected.CustomerId);
        received.Amount.ShouldBe(expected.Amount);
    }

    // ── Session-specific tests (complement the shared ConsumerTestBase tests) ─

    /// <summary>
    /// Verifies that the publisher sets <c>SessionId</c> equal to the event's
    /// <c>CustomerId</c> and that it is surfaced on the <see cref="MessageContext"/>
    /// the consumer receives.
    /// </summary>
    [Fact]
    public async Task PublishedSessionedMessage_SessionId_MatchesCustomerId()
    {
        CustomerPaymentCreatedEvent @event = NewEvent();

        (_, MessageContext ctx) = await PublishAndConsumeAsync(@event, TestContext.Current.CancellationToken);

        ctx.SessionId.ShouldBe(@event.CustomerId);
    }

    /// <summary>
    /// Two messages with the same <c>CustomerId</c> (same session) must arrive at the
    /// consumer in the order they were published.
    /// </summary>
    [Fact]
    public async Task TwoMessagesInSameSession_AreDeliveredInPublishOrder()
    {
        await DrainAsync();

        string sharedCustomerId = $"cust_{Guid.NewGuid():N}";
        var first = new CustomerPaymentCreatedEvent(Guid.NewGuid(), DateTime.UtcNow, sharedCustomerId, 10m);
        var second = new CustomerPaymentCreatedEvent(Guid.NewGuid(), DateTime.UtcNow, sharedCustomerId, 20m);

        TestConsumerCapture<CustomerPaymentCreatedEvent> capture =
            GetRequiredService<TestConsumerCapture<CustomerPaymentCreatedEvent>>();

        // Publish in order and collect exactly two deliveries.
        await PublishAsync(first, TestContext.Current.CancellationToken);
        await PublishAsync(second, TestContext.Current.CancellationToken);

        (CustomerPaymentCreatedEvent receivedFirst, _) =
            await capture.ReadAsync(ReceiveTimeout, TestContext.Current.CancellationToken);
        (CustomerPaymentCreatedEvent receivedSecond, _) =
            await capture.ReadAsync(ReceiveTimeout, TestContext.Current.CancellationToken);

        // Both must be for the shared session and in the original order.
        receivedFirst.CustomerId.ShouldBe(sharedCustomerId);
        receivedSecond.CustomerId.ShouldBe(sharedCustomerId);
        receivedFirst.Id.ShouldBe(first.Id);
        receivedSecond.Id.ShouldBe(second.Id);
    }
}
