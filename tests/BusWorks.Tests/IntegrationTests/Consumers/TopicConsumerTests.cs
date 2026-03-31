using System.Reflection;
using Azure.Messaging.ServiceBus;
using BusWorks.BackgroundServices;

namespace BusWorks.Tests.IntegrationTests.Consumers;

[NotInParallel]
[InheritsTests]
internal sealed partial class TopicConsumerTests
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

    protected override async Task ProvisionEntitiesAsync()
    {
        await EnsureTopicExistsAsync(TopicName);
        await EnsureSubscriptionExistsAsync(TopicName, SubscriptionName, o => o.MaxDeliveryCount = MaxDeliveryCount);
        await EnsureSubscriptionExistsAsync(TopicName, FanOutSubscriptionName);
    }
    

    protected override ParkingSpotStatusChangedEvent NewEvent() =>
        new(Guid.NewGuid(), DateTime.UtcNow, "B-07", "Available", "lot_north_a3");

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

    protected override async Task<ParkingSpotStatusChangedEvent> PublishAndConsumeAsync(
        ParkingSpotStatusChangedEvent @event,
        CancellationToken cancellationToken = default)
    {
        ServiceBusReceivedMessage raw = await PublishAndReceiveRawAsync(@event, cancellationToken);

        var tcs = new TaskCompletionSource<ParkingSpotStatusChangedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Instantiate directly — exercises the real BuildTypedProcessor<T> deserialization
        // and MessageContext mapping path. BuildTypedProcessor is internal and visible here
        // via InternalsVisibleTo.
        var consumer = new CapturingParkingSpotConsumer(tcs);
#pragma warning disable S3011
        var processor = (Func<ServiceBusReceivedMessage, CancellationToken, Task>)
            typeof(ServiceBusProcessorBackgroundService)
                .GetMethod("BuildTypedProcessor", BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(typeof(ParkingSpotStatusChangedEvent))
                .Invoke(null, [consumer])!;
#pragma warning restore S3011
        await processor(raw, cancellationToken);

        return await tcs.Task;
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

    protected override async Task AssertDeserializedEventAsync(
        ParkingSpotStatusChangedEvent expected,
        ParkingSpotStatusChangedEvent received)
    {
        await Assert.That(received.Id).IsEqualTo(expected.Id);
        await Assert.That(received.OccurredOnUtc).IsEqualTo(expected.OccurredOnUtc);
        await Assert.That(received.SpotCode).IsEqualTo(expected.SpotCode);
        await Assert.That(received.Status).IsEqualTo(expected.Status);
        await Assert.That(received.ParkingLotId).IsEqualTo(expected.ParkingLotId);
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

    [Test]
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
        await PublishAsync(@event);

        ServiceBusReceivedMessage? fromPrimary =
            await primaryReceiver.ReceiveMessageAsync(ReceiveTimeout);

        ServiceBusReceivedMessage? fromFanOut =
            await fanOutReceiver.ReceiveMessageAsync(ReceiveTimeout);

        // Assert — both subscriptions received an independent copy of the same message.
        await Assert.That(fromPrimary).IsNotNull();
        await Assert.That(fromFanOut).IsNotNull();
        await Assert.That(fromPrimary!.MessageId).IsEqualTo(@event.Id.ToString());
        await Assert.That(fromFanOut!.MessageId).IsEqualTo(@event.Id.ToString());
    }
}
