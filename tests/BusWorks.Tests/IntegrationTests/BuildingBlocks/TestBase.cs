using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using BusWorks.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core.Interfaces;

namespace BusWorks.Tests.IntegrationTests.BuildingBlocks;

public abstract class TestBase : IAsyncInitializer
{
    /// <summary>
    /// Shared factory instance for the full test session.
    /// TUnit injects this automatically — do not create a new instance manually.
    /// </summary>
    [ClassDataSource<EventBusHostFactory>(Shared = SharedType.PerTestSession)]
    public required EventBusHostFactory Factory { get; init; } = null!;

    /// <summary>The DI container built by the host.</summary>
    public IServiceProvider Services => Factory.Services;

    /// <summary>
    /// The emulator container for tests that need direct broker access
    /// (e.g. sending a raw <see cref="ServiceBusMessage"/> and then asserting a consumer received it).
    /// </summary>
    public AzureServiceBusEmulatorContainer Emulator => Factory.Emulator;

    /// <summary>Resolves a required service from the shared DI container.</summary>
    public T GetRequiredService<T>() where T : notnull =>
        Services.GetRequiredService<T>();

    /// <summary>
    /// Resolves the registered <see cref="IEventBusPublisher"/> and publishes
    /// <paramref name="event"/> — shorthand for the most common "publish, then assert" pattern.
    /// </summary>
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent =>
        GetRequiredService<IEventBusPublisher>().PublishAsync(@event, cancellationToken);
    
    /// <summary>
    /// Called by TUnit before the first test in the derived class runs.
    /// Invokes <see cref="ProvisionEntitiesAsync"/> so entity creation is always
    /// performed after the emulator container is fully started.
    /// Override to add per-class setup beyond entity provisioning — always call <c>base.InitializeAsync()</c>.
    /// </summary>
    public virtual Task InitializeAsync() => ProvisionEntitiesAsync();
    
    // ...existing code...

    protected virtual Task ProvisionEntitiesAsync() => Task.CompletedTask;
    
    // ...existing code...

    public async Task EnsureQueueExistsAsync(
        string queueName,
        Action<CreateQueueOptions>? configure = null)
    {
        if (await Emulator.AdminClient.QueueExistsAsync(queueName))
            return;

        var options = new CreateQueueOptions(queueName)
        {
            // The Azure Service Bus emulator caps MaxDeliveryCount at 10 (real Azure allows 2000).
            // Use the emulator maximum so application-level dead-lettering still has headroom
            // to fire before the broker's own limit.
            MaxDeliveryCount = 3,
            LockDuration = TimeSpan.FromSeconds(30),
            DefaultMessageTimeToLive = TimeSpan.FromMinutes(5)
        };

        configure?.Invoke(options);

        try
        {
            await Emulator.AdminClient.CreateQueueAsync(options);
        }
        catch (ServiceBusException ex)
            when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
        {
            // A parallel InitializeAsync call won the race and created the queue first.
            // The entity exists — this is the desired end state, so treat it as success.
        }
    }

    /// <summary>
    /// Creates <paramref name="topicName"/> in the emulator if it does not already exist.
    /// Pass <paramref name="configure"/> to override any option.
    /// </summary>
    public async Task EnsureTopicExistsAsync(
        string topicName,
        Action<CreateTopicOptions>? configure = null)
    {
        if (await Emulator.AdminClient.TopicExistsAsync(topicName))
            return;

        var options = new CreateTopicOptions(topicName)
        {
            DefaultMessageTimeToLive = TimeSpan.FromMinutes(5)
        };

        configure?.Invoke(options);

        await Emulator.AdminClient.CreateTopicAsync(options);
    }

    /// <summary>
    /// Creates a subscription on <paramref name="topicName"/> if it does not already exist.
    /// The topic itself must be created first via <see cref="EnsureTopicExistsAsync"/>.
    /// Pass <paramref name="configure"/> to override any option (e.g. add a correlation filter).
    /// </summary>
    public async Task EnsureSubscriptionExistsAsync(
        string topicName,
        string subscriptionName,
        Action<CreateSubscriptionOptions>? configure = null)
    {
        if (await Emulator.AdminClient.SubscriptionExistsAsync(topicName, subscriptionName))
            return;

        var options = new CreateSubscriptionOptions(topicName, subscriptionName)
        {
            // See EnsureQueueExistsAsync — emulator caps MaxDeliveryCount at 10.
            MaxDeliveryCount = 2,
            LockDuration = TimeSpan.FromSeconds(30),
            DefaultMessageTimeToLive = TimeSpan.FromMinutes(5)
        };

        configure?.Invoke(options);

        try
        {
            await Emulator.AdminClient.CreateSubscriptionAsync(options);
        }
        catch (ServiceBusException ex)
            when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists) { }
    }
}


