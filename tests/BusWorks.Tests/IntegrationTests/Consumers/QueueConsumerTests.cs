using Azure.Messaging.ServiceBus;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;
using Shouldly;
using Xunit;

namespace BusWorks.Tests.IntegrationTests.Consumers;

public sealed partial class QueueConsumerTests(EventBusHostFactory factory)
    : ConsumerTestBase<QueueConsumerTests.ParkingReservationCreatedEvent>(factory)
{
    private const string QueueName = "parking-reservation-created";


    protected override ParkingReservationCreatedEvent NewEvent() =>
        new(Guid.NewGuid(), DateTime.UtcNow, "A-12", "usr_7f8b91c2", 3.50m);

    protected override Type ConsumerType => typeof(CapturingParkingReservationConsumer);

    protected override ServiceBusReceiver CreateDeadLetterReceiver() =>
        Emulator.Client.CreateReceiver(
            QueueName,
            new ServiceBusReceiverOptions
            {
                SubQueue     = SubQueue.DeadLetter,
                ReceiveMode  = ServiceBusReceiveMode.ReceiveAndDelete
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

    /// <summary>
    /// Verifies that the consumer correctly deserializes a message whose JSON property
    /// names do not match the expected casing. Sends a raw JSON body directly to the queue
    /// so the background service — not a hand-rolled test helper — exercises the full
    /// deserialization and <see cref="BusWorks.Abstractions.Consumer.IConsumeContext{T}"/>
    /// mapping pipeline.
    /// </summary>
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
            MessageId   = expectedId.ToString()
        }, TestContext.Current.CancellationToken);

        // Act — the background service picks up the message, deserializes it via the real
        // consumer pipeline, and writes it to the capture channel.
        (ParkingReservationCreatedEvent received, _) =
            await GetRequiredService<TestConsumerCapture<ParkingReservationCreatedEvent>>()
                .ReadAsync(ReceiveTimeout, TestContext.Current.CancellationToken);

        // Assert
        received.Id.ShouldBe(expectedId);
        received.SpotCode.ShouldBe("B-07");
        received.UserId.ShouldBe("usr_casetest");
        received.HourlyRate.ShouldBe(5.00m);
    }
}
