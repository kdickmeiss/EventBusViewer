using Azure.Messaging.ServiceBus;
using BusWorks.BackgroundServices;

namespace BusWorks.Tests.IntegrationTests.Consumers;

[NotInParallel]
[InheritsTests]
internal sealed partial class QueueConsumerTests
    : ConsumerTestBase<QueueConsumerTests.ParkingReservationCreatedEvent>
{
    private const string QueueName = "parking-reservation-created";

    protected override async Task ProvisionEntitiesAsync()
    {
        await EnsureQueueExistsAsync(QueueName, o => o.MaxDeliveryCount = MaxDeliveryCount);
    }

    protected override ParkingReservationCreatedEvent NewEvent() =>
        new(Guid.NewGuid(), DateTime.UtcNow, "A-12", "usr_7f8b91c2", 3.50m);

    /// <inheritdoc />
    /// <remarks>
    /// The receiver is created <b>before</b> the publish call to ensure no delivery
    /// window can be missed if the broker delivers extremely fast.
    /// </remarks>
    protected override async Task<ServiceBusReceivedMessage> PublishAndReceiveRawAsync(
        ParkingReservationCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        await using ServiceBusReceiver receiver = CreateDeleteReceiver();

        await PublishAsync(@event, cancellationToken);

        ServiceBusReceivedMessage? raw = await receiver.ReceiveMessageAsync(
            ReceiveTimeout, cancellationToken);

        return raw ?? throw new InvalidOperationException(
            $"No message arrived on queue '{QueueName}' within {ReceiveTimeout.TotalSeconds} s.");
    }

    protected override async Task<ParkingReservationCreatedEvent> PublishAndConsumeAsync(
        ParkingReservationCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        ServiceBusReceivedMessage raw = await PublishAndReceiveRawAsync(@event, cancellationToken);

        var tcs = new TaskCompletionSource<ParkingReservationCreatedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Instantiate the consumer directly — bypasses DI/background service but exercises
        // the real BuildTypedProcessor<T> deserialization and MessageContext mapping path.
        var consumer = new CapturingParkingReservationConsumer(tcs);
        Func<ServiceBusReceivedMessage, CancellationToken, Task> processor =
            ServiceBusMessageProcessorBuilder.BuildTypedProcessor(consumer);
        await processor(raw, cancellationToken);

        return await tcs.Task;
    }

    protected override ServiceBusReceiver CreateDeleteReceiver() =>
        Emulator.Client.CreateReceiver(
            QueueName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });

    protected override ServiceBusReceiver CreatePeekLockReceiver() =>
        Emulator.Client.CreateReceiver(
            QueueName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });

    protected override ServiceBusReceiver CreateDeadLetterReceiver() =>
        Emulator.Client.CreateReceiver(
            QueueName,
            new ServiceBusReceiverOptions
            {
                SubQueue = SubQueue.DeadLetter,
                ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
            });

    protected override async Task AssertDeserializedEventAsync(
        ParkingReservationCreatedEvent expected,
        ParkingReservationCreatedEvent received)
    {
        await Assert.That(received.Id).IsEqualTo(expected.Id);
        await Assert.That(received.OccurredOnUtc).IsEqualTo(expected.OccurredOnUtc);
        await Assert.That(received.SpotCode).IsEqualTo(expected.SpotCode);
        await Assert.That(received.UserId).IsEqualTo(expected.UserId);
        await Assert.That(received.HourlyRate).IsEqualTo(expected.HourlyRate);
    }

    [Test]
    public async Task Consumer_Deserializes_RawMessage_WithMixedCasePropertyNames()
    {
        // Arrange
        var expectedId = Guid.NewGuid();
        DateTime expectedOccurredOn = DateTime.UtcNow;

        string body = $$"""
            {
              "ID": "{{expectedId}}",
              "occurredonutc": "{{expectedOccurredOn:O}}",
              "SPOTCODE": "B-07",
              "userid": "usr_casetest",
              "HourlyRate": 5.00
            }
            """;

        await using ServiceBusSender sender = Emulator.Client.CreateSender(QueueName);
        await sender.SendMessageAsync(new ServiceBusMessage(body)
        {
            ContentType = "application/json",
            MessageId = expectedId.ToString()
        });

        await using ServiceBusReceiver receiver = Emulator.Client.CreateReceiver(
            QueueName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });

        // Act
        ServiceBusReceivedMessage? raw = await receiver.ReceiveMessageAsync(ReceiveTimeout);

        await Assert.That(raw).IsNotNull();

        var tcs = new TaskCompletionSource<ParkingReservationCreatedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new CapturingParkingReservationConsumer(tcs);
        Func<ServiceBusReceivedMessage, CancellationToken, Task> processor =
            ServiceBusMessageProcessorBuilder.BuildTypedProcessor(consumer);
        await processor(raw!, CancellationToken.None);

        ParkingReservationCreatedEvent received = await tcs.Task;

        // Assert
        await Assert.That(received.Id).IsEqualTo(expectedId);
        await Assert.That(received.SpotCode).IsEqualTo("B-07");
        await Assert.That(received.UserId).IsEqualTo("usr_casetest");
        await Assert.That(received.HourlyRate).IsEqualTo(5.00m);
    }
}
