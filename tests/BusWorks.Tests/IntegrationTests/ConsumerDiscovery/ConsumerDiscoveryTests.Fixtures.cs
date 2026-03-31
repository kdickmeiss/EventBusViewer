using Azure.Messaging.ServiceBus;
using BusWorks.Attributes;
using BusWorks.Consumer;

namespace BusWorks.Tests.IntegrationTests.ConsumerDiscovery;

internal sealed partial class ConsumerDiscoveryTests
{
    [QueueRoute("integration-test-queue")]
    private sealed record IntegrationQueueEvent(Guid Id, DateTime OccurredOnUtc)
        : IIntegrationEvent;
    
    /// <summary>
    /// A well-formed consumer: concrete, attributed, message type has a matching
    /// [QueueRoute]. The discovery scan must include this type.
    /// </summary>
    [ServiceBusQueue]
    private sealed class ConcreteIntegrationConsumer : ServiceBusConsumer<IntegrationQueueEvent>
    {
        protected override Task ProcessMessageAsync(
            IntegrationQueueEvent message,
            ServiceBusReceivedMessage originalMessage,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
    
    /// <summary>
    /// Abstract consumers cannot be instantiated — the discovery scan must exclude them.
    /// </summary>
    private abstract class AbstractIntegrationConsumer
        : ServiceBusConsumer<IntegrationQueueEvent>;

    /// <summary>
    /// A plain class that does not inherit <c>ServiceBusConsumer</c>; the scan must exclude it.
    /// </summary>
    private sealed class NotAConsumer;
}

