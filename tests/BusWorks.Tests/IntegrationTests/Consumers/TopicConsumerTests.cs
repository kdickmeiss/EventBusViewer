using Azure.Messaging.ServiceBus;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;
using Shouldly;
using Xunit;

namespace BusWorks.Tests.IntegrationTests.Consumers;

public sealed partial class TopicConsumerTests(EventBusHostFactory factory)
    : ConsumerTestBase<TopicConsumerTests.ParkingSpotStatusChangedEvent>(factory)
{
    private const string TopicName = "parking-spot-status-changed";

    /// <summary>Primary subscription — consumed by the background service.</summary>
    private const string SubscriptionName = "spot-consumer-tests";

    /// <summary>
    /// Second subscription provisioned solely to verify fan-out delivery.
    /// No consumer is registered for it, so it is never consumed by the background service —
    /// tests read from it directly with a raw receiver.
    /// </summary>
    private const string FanOutSubscriptionName = "spot-analytics-tests";

    public override async ValueTask InitializeAsync()
    {
        // The primary topic and its subscription are pre-provisioned by EventBusHostFactory
        // (it scans CapturingParkingSpotConsumer and creates both automatically).
        // The fan-out subscription has no registered consumer so EventBusHostFactory skips
        // it — provision it here, which is idempotent after the first test run.
        await EnsureSubscriptionExistsAsync(TopicName, FanOutSubscriptionName);
    }

    protected override ParkingSpotStatusChangedEvent NewEvent() =>
        new(Guid.NewGuid(), DateTime.UtcNow, "B-07", "Available", "lot_north_a3");

    protected override Type ConsumerType => typeof(CapturingParkingSpotConsumer);

    protected override ServiceBusReceiver CreateDeadLetterReceiver() =>
        Emulator.Client.CreateReceiver(
            TopicName,
            SubscriptionName,
            new ServiceBusReceiverOptions
            {
                SubQueue    = SubQueue.DeadLetter,
                ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
            });

    protected override void AssertDeserializedEvent(
        ParkingSpotStatusChangedEvent expected,
        ParkingSpotStatusChangedEvent received)
    {
        received.Id.ShouldBe(expected.Id);
        received.OccurredOnUtc.ShouldBe(expected.OccurredOnUtc);
        received.SpotCode.ShouldBe(expected.SpotCode);
        received.Status.ShouldBe(expected.Status);
        received.ParkingLotId.ShouldBe(expected.ParkingLotId);
    }

    /// <summary>
    /// Drains the capture channel (primary subscription, managed by background service)
    /// and also drains the fan-out subscription via a raw receiver, since no background
    /// service processes it.
    /// </summary>
    protected override async Task DrainAsync()
    {
        await base.DrainAsync();
        await DrainSubscriptionAsync(FanOutSubscriptionName);
    }

    private async Task DrainSubscriptionAsync(string subscriptionName)
    {
        await using ServiceBusReceiver drainer = Emulator.Client.CreateReceiver(
            TopicName,
            subscriptionName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });

        IReadOnlyList<ServiceBusReceivedMessage> batch;
        do
        {
            batch = await drainer.ReceiveMessagesAsync(
                maxMessages: 100, maxWaitTime: TimeSpan.FromMilliseconds(300));
        }
        while (batch.Count > 0);
    }

    [Fact]
    public async Task PublishedMessage_IsDeliveredToAllSubscriptions_Independently()
    {
        ParkingSpotStatusChangedEvent @event = NewEvent();

        await DrainAsync();

        // Open the fan-out receiver before publishing so no delivery window is missed.
        // The fan-out subscription has no background-service processor, so the broker copy
        // will wait until we read it — there is no race with the primary subscription.
        // (Even if the background service writes the primary to the capture channel first,
        // the fan-out copy sits independently on the broker until ReceiveMessageAsync.)
        await using ServiceBusReceiver fanOutReceiver = Emulator.Client.CreateReceiver(
            TopicName,
            FanOutSubscriptionName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });

        await PublishAsync(@event, TestContext.Current.CancellationToken);

        (ParkingSpotStatusChangedEvent primary, _) =
            await GetRequiredService<TestConsumerCapture<ParkingSpotStatusChangedEvent>>()
                .ReadAsync(ReceiveTimeout, TestContext.Current.CancellationToken);

        ServiceBusReceivedMessage? fromFanOut =
            await fanOutReceiver.ReceiveMessageAsync(ReceiveTimeout, TestContext.Current.CancellationToken);

        // Both subscriptions received an independent copy of the same message.
        primary.Id.ShouldBe(@event.Id);
        fromFanOut.ShouldNotBeNull();
        fromFanOut.MessageId.ShouldBe(@event.Id.ToString());
    }
}
