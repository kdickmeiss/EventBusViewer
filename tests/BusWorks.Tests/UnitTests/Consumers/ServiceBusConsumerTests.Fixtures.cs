using BusWorks.Abstractions;
using BusWorks.Abstractions.Consumer;

namespace BusWorks.Tests.UnitTests.Consumers;

internal sealed partial class ServiceBusConsumerTests
{
    private sealed record TestEvent(Guid Id, DateTime OccurredOnUtc, string? Name, int Value)
        : IIntegrationEvent;

    /// <summary>
    /// Records everything the framework passes into <see cref="IConsumer{T}.Consume"/>
    /// so tests can assert on deserialization, metadata mapping, and token forwarding.
    /// </summary>
    private sealed class TrackingConsumer : IConsumer<TestEvent>
    {
        public TestEvent? ReceivedMessage { get; private set; }
        public MessageContext? ReceivedMetadata { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task Consume(IConsumeContext<TestEvent> context)
        {
            ReceivedMessage = context.Message;
            ReceivedMetadata = context.Metadata;
            ReceivedCancellationToken = context.CancellationToken;
            return Task.CompletedTask;
        }
    }

    /// <summary>Always throws — used to verify exception propagation out of the pipeline.</summary>
    private sealed class FaultingConsumer : IConsumer<TestEvent>
    {
        public static readonly InvalidOperationException Error = new("processing failed");

        public Task Consume(IConsumeContext<TestEvent> context) => Task.FromException(Error);
    }
}
