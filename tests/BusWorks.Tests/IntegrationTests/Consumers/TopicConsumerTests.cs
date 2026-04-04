using Azure.Messaging.ServiceBus;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;
using Shouldly;
using Xunit;

namespace BusWorks.Tests.IntegrationTests.Consumers;

public sealed partial class TopicConsumerTests
    : ConsumerTestBase<TopicConsumerTests.ParkingSpotStatusChangedEvent>
{
    private const string TopicName = "parking-spot-status-changed";

    /// <summary>Primary subscription — used by the majority of tests.</summary>
    private const string SubscriptionName = "spot-consumer-tests";

    /// <summary>
    /// Second subscription provisioned solely to verify fan-out delivery:
    /// both subscriptions must independently receive the same published message.
    /// </summary>
    private const string FanOutSubscriptionName = "spot-analytics-tests";

    public TopicConsumerTests(EventBusHostFactory factory) : base(factory) { }

    public override async ValueTask InitializeAsync()
    {
        await EnsureTopicExistsAsync(TopicName);
        await EnsureSubscriptionExistsAsync(TopicName, SubscriptionName, o => o.MaxDeliveryCount = MaxDeliveryCount);
        await EnsureSubscriptionExistsAsync(TopicName, FanOutSubscriptionName);
    }
    
    protected override ParkingSpotStatusChangedEvent NewEvent() =>
        new(Guid.NewGuid(), DateTime.UtcNow, "B-07", "Available", "lot_north_a3");

    /// <inheritdoc />
    protected override Type ConsumerType => typeof(CapturingParkingSpotConsumer);

    /// <inheritdoc />
    /// <remarks>
    /// Drains both subscriptions before opening the receiver so that neither stale
    /// fan-out copies nor leftover messages from previous tests pollute the result.
    /// The receiver is opened <b>before</b> the publish call to close any delivery window.
    /// </remarks>
    protected override async Task<ServiceBusReceivedMessage> PublishAndReceiveRawAsync(
        ParkingSpotStatusChangedEvent @event,
        CancellationToken cancellationToken = default)
    {
        await DrainAsync();

        await using ServiceBusReceiver receiver = CreateDeleteReceiver();

        await PublishAsync(@event, cancellationToken);

        ServiceBusReceivedMessage? raw = await receiver.ReceiveMessageAsync(
            ReceiveTimeout, cancellationToken);

        return raw ?? throw new InvalidOperationException(
            $"No message arrived on subscription '{TopicName}/{SubscriptionName}' " +
            $"within {ReceiveTimeout.TotalSeconds} s.");
    }


    protected override ServiceBusReceiver CreateDeleteReceiver() =>
        Emulator.Client.CreateReceiver(
            TopicName,
            SubscriptionName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });

    protected override ServiceBusReceiver CreatePeekLockReceiver() =>
        Emulator.Client.CreateReceiver(
            TopicName,
            SubscriptionName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });

    protected override ServiceBusReceiver CreateDeadLetterReceiver() =>
        Emulator.Client.CreateReceiver(
            TopicName,
            SubscriptionName,
            new ServiceBusReceiverOptions
            {
                SubQueue = SubQueue.DeadLetter,
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
    /// Drains both active subscriptions so that fan-out copies left by a previous test
    /// never interfere with the next receive call.
    /// </summary>
    protected override async Task DrainAsync()
    {
        await DrainSubscriptionAsync(SubscriptionName);
        await DrainSubscriptionAsync(FanOutSubscriptionName);
    }

    /// <summary>
    /// Removes all currently available messages from <paramref name="subscriptionName"/>
    /// using <see cref="ServiceBusReceiveMode.ReceiveAndDelete"/> mode.
    /// </summary>
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
                maxMessages: 100,
                maxWaitTime: TimeSpan.FromMilliseconds(500));
        }
        while (batch.Count > 0);
    }

    [Fact]
    public async Task PublishedMessage_IsDeliveredToAllSubscriptions_Independently()
    {
        // Arrange
        ParkingSpotStatusChangedEvent @event = NewEvent();

        await DrainAsync();

        // Open both receivers before publishing so neither misses the delivery window.
        await using ServiceBusReceiver primaryReceiver = Emulator.Client.CreateReceiver(
            TopicName,
            SubscriptionName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });

        await using ServiceBusReceiver fanOutReceiver = Emulator.Client.CreateReceiver(
            TopicName,
            FanOutSubscriptionName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });

        // Act
        await PublishAsync(@event, TestContext.Current.CancellationToken);

        ServiceBusReceivedMessage? fromPrimary =
            await primaryReceiver.ReceiveMessageAsync(ReceiveTimeout, TestContext.Current.CancellationToken);

        ServiceBusReceivedMessage? fromFanOut =
            await fanOutReceiver.ReceiveMessageAsync(ReceiveTimeout, TestContext.Current.CancellationToken);

        // Assert — both subscriptions received an independent copy of the same message.
        fromPrimary.ShouldNotBeNull();
        fromFanOut.ShouldNotBeNull();
        fromPrimary.MessageId.ShouldBe(@event.Id.ToString());
        fromFanOut.MessageId.ShouldBe(@event.Id.ToString());
    }
}
