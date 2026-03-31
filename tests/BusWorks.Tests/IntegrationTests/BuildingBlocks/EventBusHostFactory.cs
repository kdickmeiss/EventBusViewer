using BusWorks.BackgroundServices;
using BusWorks.Options;
using BusWorks.Publisher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using TUnit.Core.Interfaces;

namespace BusWorks.Tests.IntegrationTests.BuildingBlocks;

public sealed class EventBusHostFactory : IAsyncInitializer, IAsyncDisposable
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
    public async Task InitializeAsync()
    {
        // Start the emulator container and provision all entities first so that any
        // component resolved from the DI container can immediately communicate with
        // the broker (e.g. ServiceBusPublisher sending a message in a test).
        await Emulator.InitializeAsync();

        _host = new HostBuilder()
            .ConfigureAppConfiguration(config =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{EventBusOptions.SectionName}:AuthenticationType"] =
                        nameof(EventBusAuthenticationType.ConnectionString),
                    [$"{EventBusOptions.SectionName}:ConnectionString:ConnectionString"] =
                        Emulator.ConnectionString,
                    [$"{EventBusOptions.SectionName}:MaxConcurrentCalls"] = "10",
                    [$"{EventBusOptions.SectionName}:MaxConcurrentSessions"] = "8",
                    [$"{EventBusOptions.SectionName}:MaxConcurrentCallsPerSession"] = "1",
                }))
            .ConfigureServices((context, services) =>
            {
                services.Configure<EventBusOptions>(
                    context.Configuration.GetSection(EventBusOptions.SectionName));

                services
                    // Reuse the same ServiceBusClient that the emulator fixture owns so
                    // there is exactly one AMQP connection shared across the whole session.
                    .AddSingleton(Emulator.Client)
                    .AddSingleton(new ServiceBusAssemblyRegistry(typeof(EventBusHostFactory).Assembly))
                    .AddHostedService<ServiceBusProcessorBackgroundService>()
                    .AddSingleton<IEventBusPublisher, ServiceBusPublisher>();

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

