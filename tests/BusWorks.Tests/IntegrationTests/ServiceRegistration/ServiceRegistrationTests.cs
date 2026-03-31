using Azure.Messaging.ServiceBus;
using BusWorks.Options;
using BusWorks.Publisher;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace BusWorks.Tests.IntegrationTests.ServiceRegistration;

/// <summary>
/// Integration tests that verify all EventBus services are correctly wired up in the DI
/// container.
/// </summary>
/// <remarks>
/// A single <see cref="EventBusHostFactory"/> is shared for the entire test session
/// (<see cref="SharedType.PerTestSession"/>), mirroring xUnit's <c>ICollectionFixture&lt;T&gt;</c>
/// pattern. The host is built once; all tests resolve services from the same container.
/// </remarks>
internal sealed class ServiceRegistrationTests : TestBase
{
    [Test]
    public async Task IEventBusPublisher_IsRegistered_AndResolvesSuccessfully()
    {
        // Act
        IEventBusPublisher publisher = GetRequiredService<IEventBusPublisher>();

        // Assert
        await Assert.That(publisher).IsNotNull();
    }

    [Test]
    public async Task IEventBusPublisher_ResolvesToServiceBusPublisher()
    {
        // ServiceBusPublisher is internal — visible here via InternalsVisibleTo.

        // Act
        IEventBusPublisher publisher = GetRequiredService<IEventBusPublisher>();

        // Assert
        await Assert.That(publisher is ServiceBusPublisher).IsTrue();
    }

    [Test]
    public async Task IEventBusPublisher_IsRegisteredAsSingleton_ReturnsSameInstanceOnEveryResolve()
    {
        // Act
        IEventBusPublisher first = GetRequiredService<IEventBusPublisher>();
        IEventBusPublisher second = GetRequiredService<IEventBusPublisher>();

        // Assert
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }
    
    [Test]
    public async Task ServiceBusClient_IsRegistered_AndResolvesSuccessfully()
    {
        // Act
        ServiceBusClient client = GetRequiredService<ServiceBusClient>();

        // Assert
        await Assert.That(client).IsNotNull();
    }

    [Test]
    public async Task ServiceBusClient_IsRegisteredAsSingleton_ReturnsSameInstanceOnEveryResolve()
    {
        // Act
        ServiceBusClient first = GetRequiredService<ServiceBusClient>();
        ServiceBusClient second = GetRequiredService<ServiceBusClient>();

        // Assert
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task ServiceBusAssemblyRegistry_IsRegistered_AndResolvesSuccessfully()
    {
        // Act
        ServiceBusAssemblyRegistry registry = GetRequiredService<ServiceBusAssemblyRegistry>();

        // Assert
        await Assert.That(registry).IsNotNull();
    }
    
    [Test]
    public async Task Tracer_IsRegistered_AndResolvesSuccessfully()
    {
        // Act
        Tracer tracer = GetRequiredService<Tracer>();

        // Assert
        await Assert.That(tracer).IsNotNull();
    }
    
    [Test]
    public async Task EventBusOptions_AuthenticationType_BindsFromConfiguration()
    {
        // Act
        IOptions<EventBusOptions> options = GetRequiredService<IOptions<EventBusOptions>>();

        // Assert
        await Assert.That(options.Value.AuthenticationType)
            .IsEqualTo(EventBusAuthenticationType.ConnectionString);
    }

    [Test]
    public async Task EventBusOptions_ConnectionString_BindsFromConfiguration()
    {
        // Act
        IOptions<EventBusOptions> options = GetRequiredService<IOptions<EventBusOptions>>();

        // Assert
        await Assert.That(options.Value.ConnectionString?.ConnectionString)
            .IsEqualTo(Emulator.ConnectionString);
    }

    [Test]
    public async Task EventBusOptions_MaxConcurrentCalls_BindsFromConfiguration()
    {
        // Act
        IOptions<EventBusOptions> options = GetRequiredService<IOptions<EventBusOptions>>();

        // Assert
        await Assert.That(options.Value.MaxConcurrentCalls).IsEqualTo(10);
    }

    [Test]
    public async Task EventBusOptions_MaxConcurrentSessions_BindsFromConfiguration()
    {
        // Act
        IOptions<EventBusOptions> options = GetRequiredService<IOptions<EventBusOptions>>();

        // Assert
        await Assert.That(options.Value.MaxConcurrentSessions).IsEqualTo(8);
    }

    [Test]
    public async Task EventBusOptions_MaxConcurrentCallsPerSession_BindsFromConfiguration()
    {
        // Act
        IOptions<EventBusOptions> options = GetRequiredService<IOptions<EventBusOptions>>();

        // Assert
        await Assert.That(options.Value.MaxConcurrentCallsPerSession).IsEqualTo(1);
    }
}
