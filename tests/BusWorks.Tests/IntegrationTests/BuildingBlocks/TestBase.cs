using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions;
using BusWorks.Abstractions.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusWorks.Tests.IntegrationTests.BuildingBlocks;

[Collection(nameof(IntegrationTestCollection))]
[Trait("Category", "Integration")]
public abstract class TestBase(EventBusHostFactory factory) : IAsyncLifetime
{
    /// <summary>
    /// Override to create the queues, topics, or subscriptions required by the test class.
    /// Called automatically by xUnit before each test. Entity helpers are idempotent.
    /// </summary>
    public virtual ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Shared factory instance for the full test collection.
    /// xUnit injects this automatically via constructor — the same instance is reused
    /// across all tests in the <c>IntegrationTests</c> collection.
    /// </summary>
    private EventBusHostFactory Factory { get; } = factory;

    /// <summary>The DI container built by the host.</summary>
    private IServiceProvider Services => Factory.Services;

    /// <summary>
    /// The emulator container for tests that need direct broker access
    /// (e.g. sending a raw <see cref="ServiceBusMessage"/> and then asserting a consumer received it).
    /// </summary>
    protected AzureServiceBusEmulatorContainer Emulator => Factory.Emulator;

    /// <summary>Resolves a required service from the shared DI container.</summary>
    protected T GetRequiredService<T>() where T : notnull =>
        Services.GetRequiredService<T>();

    /// <summary>
    /// Resolves the registered <see cref="IEventBusPublisher"/> and publishes
    /// <paramref name="event"/> — shorthand for the most common "publish, then assert" pattern.
    /// </summary>
    protected Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent =>
        GetRequiredService<IEventBusPublisher>().PublishAsync(@event, cancellationToken);

    /// <summary>
    /// Creates a subscription on <paramref name="topicName"/> if it does not already exist.
    /// Delegates to <see cref="EventBusHostFactory.EnsureSubscriptionAsync"/> so all
    /// provisioning uses the same options (MaxDeliveryCount, LockDuration, TTL).
    /// </summary>
    protected Task EnsureSubscriptionExistsAsync(string topicName, string subscriptionName) =>
        Factory.EnsureSubscriptionAsync(topicName, subscriptionName);
}
