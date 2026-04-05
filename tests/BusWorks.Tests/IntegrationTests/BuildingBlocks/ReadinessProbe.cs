using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Consumer;
using BusWorks.Abstractions.Events;

namespace BusWorks.Tests.IntegrationTests.BuildingBlocks;

/// <summary>
/// Synthetic event published by <see cref="EventBusHostFactory"/> immediately after
/// <c>_host.StartAsync()</c> to confirm that the
/// <c>ServiceBusProcessorBackgroundService</c> has called <c>StartProcessingAsync</c>
/// on the probe queue's processor. Never used in test assertions.
/// </summary>
[QueueRoute("busworks-readiness-probe")]
internal sealed record ReadinessProbeEvent(Guid Id, DateTime OccurredOnUtc)
    : IntegrationEvent(Id, OccurredOnUtc);

/// <summary>
/// Writes the probe event to <see cref="TestConsumerCapture{TEvent}"/> so
/// <see cref="EventBusHostFactory"/> can <c>await</c> it and then discard it.
/// </summary>
[ServiceBusQueue]
internal sealed class ReadinessProbeConsumer(
    TestConsumerCapture<ReadinessProbeEvent> capture)
    : IConsumer<ReadinessProbeEvent>
{
    public Task Consume(IConsumeContext<ReadinessProbeEvent> context) =>
        capture.WriteAsync(context.Message, context.Metadata, context.CancellationToken).AsTask();
}


