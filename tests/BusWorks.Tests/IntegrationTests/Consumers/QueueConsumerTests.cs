using Azure.Messaging.ServiceBus;
using BusWorks.BackgroundServices;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;
using Xunit;

namespace BusWorks.Tests.IntegrationTests.Consumers;

public sealed partial class QueueConsumerTests
    : ConsumerTestBase<QueueConsumerTests.ParkingReservationCreatedEvent>
{
    private const string QueueName = "parking-reservation-created";

    public QueueConsumerTests(EventBusHostFactory factory) : base(factory) { }

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
        await Task.CompletedTask; // keep the method async for interface compatibility
        Assert.Equal(expected.Id, received.Id);
        Assert.Equal(expected.OccurredOnUtc, received.OccurredOnUtc);
        Assert.Equal(expected.SpotCode, received.SpotCode);
        Assert.Equal(expected.UserId, received.UserId);
        Assert.Equal(expected.HourlyRate, received.HourlyRate);
    }

    [Fact]
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
        }, TestContext.Current.CancellationToken);

        await using ServiceBusReceiver receiver = Emulator.Client.CreateReceiver(
            QueueName,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });

        // Act
        ServiceBusReceivedMessage? raw = await receiver.ReceiveMessageAsync(ReceiveTimeout, TestContext.Current.CancellationToken);

        Assert.NotNull(raw);

        var tcs = new TaskCompletionSource<ParkingReservationCreatedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new CapturingParkingReservationConsumer(tcs);
        Func<ServiceBusReceivedMessage, CancellationToken, Task> processor =
            ServiceBusMessageProcessorBuilder.BuildTypedProcessor(consumer);
        await processor(raw, CancellationToken.None);

        ParkingReservationCreatedEvent received = await tcs.Task;

        // Assert
        Assert.Equal(expectedId, received.Id);
        Assert.Equal("B-07", received.SpotCode);
        Assert.Equal("usr_casetest", received.UserId);
        Assert.Equal(5.00m, received.HourlyRate);
    }
}
