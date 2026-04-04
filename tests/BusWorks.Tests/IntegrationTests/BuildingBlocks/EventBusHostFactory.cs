using BusWorks.Abstractions;
using BusWorks.BackgroundServices;
using BusWorks.Options;
using BusWorks.Publisher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
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

    /// <summary>The DI container for the built (not started) test host.</summary>
    public IServiceProvider Services => _host.Services;

    private IHost _host = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        // Start the emulator container and provision all entities first so that any
        // component resolved from the DI container can immediately communicate with
        // the broker (e.g. ServiceBusPublisher sending a message in a test).
        await Emulator.InitializeAsync();

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

                // Discover consumers from the test assembly — the same registry the background
                // service uses at runtime.
                var registry = new ServiceBusAssemblyRegistry(typeof(EventBusHostFactory).Assembly);

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

                // Open-generic singleton registration: one TestConsumerCapture<T> instance is
                // created per concrete event type on first resolution (e.g.
                // TestConsumerCapture<ParkingReservationCreatedEvent>).
                services.AddSingleton(typeof(TestConsumerCapture<>), typeof(TestConsumerCapture<>));

                // Mirror the real Program.cs pattern:
                //   .AddSingleton(TracerProvider.Default.GetTracer(builder.Environment.ApplicationName))
                // Using the default (no-op) provider keeps spans cheap — they are created
                // but never exported anywhere during tests.
                services.AddSingleton(
                    TracerProvider.Default.GetTracer("EventBus.AzureServiceBus.Tests"));

            })
            .Build();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _host.Dispose();
        await Emulator.DisposeAsync();
    }
}

