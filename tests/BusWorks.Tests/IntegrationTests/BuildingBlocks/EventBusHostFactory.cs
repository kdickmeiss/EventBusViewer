using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using BusWorks.Abstractions;
using BusWorks.Abstractions.Attributes;
using BusWorks.BackgroundServices;
using BusWorks.Options;
using BusWorks.Publisher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using System.Reflection;
using Xunit;

namespace BusWorks.Tests.IntegrationTests.BuildingBlocks;

public sealed class EventBusHostFactory : IAsyncLifetime
{
    /// <summary>
    /// The emulator fixture that manages the container and all entity provisioning.
    /// Exposed so tests that need direct broker access (publish / receive) can use
    /// <see cref="AzureServiceBusEmulatorContainer.Client"/> without re-creating a client.
    /// </summary>
    public AzureServiceBusEmulatorContainer Emulator { get; } = new();

    /// <summary>The DI container for the running test host.</summary>
    public IServiceProvider Services => _host.Services;

    private IHost _host = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await Emulator.InitializeAsync();

        // Pre-provision every broker entity that the discovered consumers require.
        // The background service calls StartProcessingAsync at host startup — the
        // entities must already exist at that point or the processor setup silently fails.
        var registry = new ServiceBusAssemblyRegistry(typeof(EventBusHostFactory).Assembly);
        await ProvisionConsumerEntitiesAsync(registry);

        _host = new HostBuilder()
            .ConfigureAppConfiguration(config =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{BusWorksOptions.SectionName}:AuthenticationType"] =
                        nameof(EventBusAuthenticationType.ConnectionString),
                    [$"{BusWorksOptions.SectionName}:ConnectionString:ConnectionString"] =
                        Emulator.ConnectionString,
                    [$"{BusWorksOptions.SectionName}:MaxConcurrentCalls"] = "10",
                    [$"{BusWorksOptions.SectionName}:MaxConcurrentSessions"] = "8",
                    [$"{BusWorksOptions.SectionName}:MaxConcurrentCallsPerSession"] = "1",
                }))
            .ConfigureServices((context, services) =>
            {
                services.Configure<BusWorksOptions>(
                    context.Configuration.GetSection(BusWorksOptions.SectionName));

                services
                    // Reuse the same ServiceBusClient that the emulator fixture owns so
                    // there is exactly one AMQP connection shared across the whole session.
                    .AddSingleton(Emulator.Client)
                    .AddSingleton(registry)
                    .AddHostedService<ServiceBusProcessorBackgroundService>()
                    .AddSingleton<IEventBusPublisher, ServiceBusPublisher>();

                // Register each discovered consumer as scoped — identical to what
                // DependencyInjection.AddBusWorksCore does in production so that
                // ServiceBusMessageProcessorBuilder.Build() can resolve them from a DI scope.
                foreach (Type consumerType in registry.GetConsumerTypes())
                    services.AddScoped(consumerType);

                // Open-generic singleton: one TestConsumerCapture<T> per concrete event type.
                services.AddSingleton(typeof(TestConsumerCapture<>), typeof(TestConsumerCapture<>));

                // Mirror the real Program.cs pattern — no-op tracer keeps spans cheap.
                services.AddSingleton(
                    TracerProvider.Default.GetTracer("EventBus.AzureServiceBus.Tests"));
            })
            .Build();

        // Start the host: fires ApplicationStarted → background service sets up processors.
        await _host.StartAsync();

        // Wait for the background service to finish calling StartProcessingAsync on every
        // processor. We do this by publishing a probe event and awaiting its consumption —
        // the probe message can only be dispatched once the processor for its queue is up,
        // so this is deterministic with no fixed sleep.
        await WaitForProcessorsReadyAsync();
    }

    /// <summary>
    /// Publishes a <see cref="ReadinessProbeEvent"/> and blocks until the
    /// <see cref="ReadinessProbeConsumer"/> captures it, proving that the
    /// <c>ServiceBusProcessorBackgroundService</c> has started the probe queue's processor.
    /// </summary>
    private async Task WaitForProcessorsReadyAsync()
    {
        var probe = new ReadinessProbeEvent(Guid.NewGuid(), DateTime.UtcNow);

        await _host.Services
            .GetRequiredService<IEventBusPublisher>()
            .PublishAsync(probe, CancellationToken.None);

        await _host.Services
            .GetRequiredService<TestConsumerCapture<ReadinessProbeEvent>>()
            .ReadAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await Emulator.DisposeAsync();
    }

    // ── Entity provisioning ────────────────────────────────────────────────

    /// <summary>
    /// Inspects every consumer type in <paramref name="registry"/> and creates any missing
    /// queues, topics, or subscriptions in the emulator. Uses high broker-level
    /// <c>MaxDeliveryCount</c> values so the entity setting never fires before the
    /// application-level enforcement in <see cref="ServiceBusProcessorBackgroundService"/>.
    /// </summary>
    private async Task ProvisionConsumerEntitiesAsync(ServiceBusAssemblyRegistry registry)
    {
        foreach (Type consumerType in registry.GetConsumerTypes())
        {
            // Skip consumers without routing attributes (e.g. unit-test stubs).
            bool hasQueueAttr = consumerType.GetCustomAttribute<ServiceBusQueueAttribute>() is not null;
            bool hasTopicAttr = consumerType.GetCustomAttribute<ServiceBusTopicAttribute>() is not null;
            if (!hasQueueAttr && !hasTopicAttr)
                continue;

            // Skip consumers whose configuration is intentionally invalid — these are unit-test
            // fixtures (e.g. NegativeDeliveryCountQueueConsumer, QueueConsumerWithTopicMessage)
            // that exist to verify error-handling in ServiceBusEndpointResolver.
            // The background service encounters the same error and handles it gracefully via
            // its own try-catch inside SetupConsumerAsync.
            ServiceBusEndpoint endpoint;
            try
            {
                endpoint = ServiceBusEndpointResolver.Resolve(consumerType);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (endpoint.IsQueue)
            {
                await EnsureQueueAsync(endpoint.QueueOrTopicName);
            }
            else
            {
                await EnsureTopicAsync(endpoint.QueueOrTopicName);
                await EnsureSubscriptionAsync(endpoint.QueueOrTopicName, endpoint.SubscriptionName!);
            }
        }
    }

    private async Task EnsureQueueAsync(string name)
    {
        if (await Emulator.AdminClient.QueueExistsAsync(name))
            return;

        try
        {
            await Emulator.AdminClient.CreateQueueAsync(new CreateQueueOptions(name)
            {
                // Set high so the broker never interferes before application-level enforcement.
                MaxDeliveryCount = 10,
                LockDuration = TimeSpan.FromSeconds(30),
                DefaultMessageTimeToLive = TimeSpan.FromMinutes(5)
            });
        }
        catch (ServiceBusException ex)
            when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
        {
            // A parallel call won the race — entity exists, which is the desired end state.
        }
    }

    private async Task EnsureTopicAsync(string name)
    {
        if (await Emulator.AdminClient.TopicExistsAsync(name))
            return;

        await Emulator.AdminClient.CreateTopicAsync(new CreateTopicOptions(name)
        {
            DefaultMessageTimeToLive = TimeSpan.FromMinutes(5)
        });
    }

    internal async Task EnsureSubscriptionAsync(string topicName, string subscriptionName)
    {
        if (await Emulator.AdminClient.SubscriptionExistsAsync(topicName, subscriptionName))
            return;

        await Emulator.AdminClient.CreateSubscriptionAsync(
            new CreateSubscriptionOptions(topicName, subscriptionName)
            {
                MaxDeliveryCount = 10,
                LockDuration = TimeSpan.FromSeconds(30),
                DefaultMessageTimeToLive = TimeSpan.FromMinutes(5)
            }); // Race — entity already created.
    }
}
