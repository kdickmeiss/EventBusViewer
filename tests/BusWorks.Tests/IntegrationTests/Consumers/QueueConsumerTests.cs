using Azure.Messaging.ServiceBus;
using BusWorks.BackgroundServices;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace BusWorks.Tests.IntegrationTests.Consumers;

public sealed partial class QueueConsumerTests(EventBusHostFactory factory)
    : ConsumerTestBase<QueueConsumerTests.ParkingReservationCreatedEvent>(factory)
{
    private const string QueueName = "parking-reservation-created";

    public override async ValueTask InitializeAsync()
    {
        await EnsureQueueExistsAsync(QueueName, o => o.MaxDeliveryCount = MaxDeliveryCount);
    }

    protected override ParkingReservationCreatedEvent NewEvent() =>
        new(Guid.NewGuid(), DateTime.UtcNow, "A-12", "usr_7f8b91c2", 3.50m);

    /// <inheritdoc />
    protected override Type ConsumerType => typeof(CapturingParkingReservationConsumer);

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

    protected override void AssertDeserializedEvent(
        ParkingReservationCreatedEvent expected,
        ParkingReservationCreatedEvent received)
    {
        received.Id.ShouldBe(expected.Id);
        received.OccurredOnUtc.ShouldBe(expected.OccurredOnUtc);
        received.SpotCode.ShouldBe(expected.SpotCode);
        received.UserId.ShouldBe(expected.UserId);
        received.HourlyRate.ShouldBe(expected.HourlyRate);
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

        ServiceBusReceivedMessage? raw = await receiver.ReceiveMessageAsync(
            ReceiveTimeout, TestContext.Current.CancellationToken);

        raw.ShouldNotBeNull();

        // Act — mirror the background service: create a DI scope, resolve the consumer from it,
        // and dispatch through the same Build() factory the background service pre-builds at startup.
        using IServiceScope scope = GetRequiredService<IServiceScopeFactory>().CreateScope();
        Func<IServiceProvider, Func<ServiceBusReceivedMessage, CancellationToken, Task>> processorFactory =
            ServiceBusMessageProcessorBuilder.Build(typeof(CapturingParkingReservationConsumer));
        await processorFactory(scope.ServiceProvider)(raw, TestContext.Current.CancellationToken);

        ParkingReservationCreatedEvent received =
            await GetRequiredService<TestConsumerCapture<ParkingReservationCreatedEvent>>()
                .ReadAsync(ReceiveTimeout, TestContext.Current.CancellationToken);

        // Assert
        received.Id.ShouldBe(expectedId);
        received.SpotCode.ShouldBe("B-07");
        received.UserId.ShouldBe("usr_casetest");
        received.HourlyRate.ShouldBe(5.00m);
    }
}
