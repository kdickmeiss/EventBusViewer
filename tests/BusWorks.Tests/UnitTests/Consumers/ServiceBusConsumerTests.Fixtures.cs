using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions;
using BusWorks.Consumer;

namespace BusWorks.Tests.UnitTests.Consumers;

internal sealed partial class ServiceBusConsumerTests
{
    private sealed record TestEvent(Guid Id, DateTime OccurredOnUtc, string? Name, int Value)
        : IIntegrationEvent;

    private sealed class TrackingConsumer : ServiceBusConsumer<TestEvent>
    {
        public TestEvent? ReceivedMessage { get; private set; }
        public ServiceBusReceivedMessage? ReceivedRawMessage { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }

        // Expose the protected property so the default-options test can assert on it.
        public JsonSerializerOptions ExposedOptions => JsonSerializerOptions;

        protected override Task ProcessMessageAsync(
            TestEvent message,
            ServiceBusReceivedMessage originalMessage,
            CancellationToken cancellationToken)
        {
            ReceivedMessage = message;
            ReceivedRawMessage = originalMessage;
            ReceivedCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class CustomOptionsConsumer : ServiceBusConsumer<TestEvent>
    {
        // Case-sensitive options: lowercase JSON "name" will NOT match PascalCase "Name" property.
        private static readonly JsonSerializerOptions CaseSensitiveOptions =
            new() { PropertyNameCaseInsensitive = false };

        protected override JsonSerializerOptions JsonSerializerOptions => CaseSensitiveOptions;

        public TestEvent? ReceivedMessage { get; private set; }

        protected override Task ProcessMessageAsync(
            TestEvent message,
            ServiceBusReceivedMessage originalMessage,
            CancellationToken cancellationToken)
        {
            ReceivedMessage = message;
            return Task.CompletedTask;
        }
    }

    private sealed class FaultingConsumer : ServiceBusConsumer<TestEvent>
    {
        public static readonly InvalidOperationException Error =
            new("processing failed");

        protected override Task ProcessMessageAsync(
            TestEvent message,
            ServiceBusReceivedMessage originalMessage,
            CancellationToken cancellationToken) =>
            Task.FromException(Error);
    }

    private sealed class TrackingRawConsumer : ServiceBusConsumer
    {
        public ServiceBusReceivedMessage? ReceivedMessage { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }

        protected override Task ProcessMessageAsync(
            ServiceBusReceivedMessage message,
            CancellationToken cancellationToken)
        {
            ReceivedMessage = message;
            ReceivedCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class FaultingRawConsumer : ServiceBusConsumer
    {
        public static readonly InvalidOperationException Error =
            new("raw processing failed");

        protected override Task ProcessMessageAsync(
            ServiceBusReceivedMessage message,
            CancellationToken cancellationToken) =>
            Task.FromException(Error);
    }
}
