using System.Reflection;
using BusWorks.Consumer;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;

namespace BusWorks.Tests.IntegrationTests.ConsumerDiscovery;

/// <summary>
/// Integration tests that verify <see cref="ServiceBusAssemblyRegistry"/> drives consumer
/// discovery correctly — using the exact same scanning predicate that
/// <c>ServiceBusProcessorBackgroundService.DiscoverConsumerTypes</c> runs at application startup.
/// </summary>
/// <remarks>
/// Shares the same <see cref="EventBusHostFactory"/> session as
/// <c>ServiceRegistrationTests</c> — no second host is built.
/// </remarks>
internal sealed partial class ConsumerDiscoveryTests : TestBase
{
    /// <summary>
    /// Mirrors the private <c>DiscoverConsumerTypes</c> method in
    /// <c>ServiceBusProcessorBackgroundService</c> so we can assert on discovery outcomes
    /// without relying on reflection into private methods.
    /// </summary>
    // S2325: False positive — GetRequiredService() accesses the Services instance property.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S2325", Justification = "Accesses instance data via GetRequiredService -> Services")]
    private IReadOnlyList<Type> DiscoverConsumers()
    {
        // IServiceBusConsumer is internal — visible here via InternalsVisibleTo.
        ServiceBusAssemblyRegistry registry = GetRequiredService<ServiceBusAssemblyRegistry>();

        return registry.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IServiceBusConsumer).IsAssignableFrom(t))
            .ToList();
    }

    [Test]
    public async Task Registry_ContainsTestAssembly()
    {
        // Arrange
        Assembly testAssembly = typeof(EventBusHostFactory).Assembly;

        // Act
        ServiceBusAssemblyRegistry registry = GetRequiredService<ServiceBusAssemblyRegistry>();

        // Assert
        await Assert.That(registry.GetAssemblies().Contains(testAssembly)).IsTrue();
    }

    [Test]
    public async Task ConcreteConsumer_InTestAssembly_IsDiscovered()
    {
        // Act
        IReadOnlyList<Type> discovered = DiscoverConsumers();

        // Assert
        await Assert.That(discovered.Contains(typeof(ConcreteIntegrationConsumer))).IsTrue();
    }

    [Test]
    public async Task AbstractConsumer_IsExcludedFromDiscovery()
    {
        // Abstract types cannot be instantiated — they must never reach the processor setup.

        // Act
        IReadOnlyList<Type> discovered = DiscoverConsumers();

        // Assert
        await Assert.That(discovered.Contains(typeof(AbstractIntegrationConsumer))).IsFalse();
    }

    [Test]
    public async Task NonConsumerClass_IsExcludedFromDiscovery()
    {
        // A plain class that does not inherit ServiceBusConsumer must be excluded.

        // Act
        IReadOnlyList<Type> discovered = DiscoverConsumers();

        // Assert
        await Assert.That(discovered.Contains(typeof(NotAConsumer))).IsFalse();
    }
    
    [Test]
    public async Task AllDiscoveredConsumers_ImplementIServiceBusConsumer()
    {
        // Every discovered type must satisfy the interface contract so the background
        // service can safely cast and call ProcessMessageInternalAsync.

        // Act
        IReadOnlyList<Type> discovered = DiscoverConsumers();

        // Assert
        foreach (Type consumerType in discovered)
        {
            await Assert.That(typeof(IServiceBusConsumer).IsAssignableFrom(consumerType)).IsTrue();
        }
    }

    [Test]
    public async Task AllDiscoveredConsumers_AreConcrete_NotAbstract()
    {
        // The background service creates instances via ActivatorUtilities — abstract types
        // would throw at runtime, so they must be filtered out at the scanning stage.

        // Act
        IReadOnlyList<Type> discovered = DiscoverConsumers();

        // Assert
        foreach (Type consumerType in discovered)
        {
            await Assert.That(consumerType.IsAbstract).IsFalse();
        }
    }

    [Test]
    public async Task AllDiscoveredConsumers_AreClasses_NotInterfacesOrValueTypes()
    {
        // Act
        IReadOnlyList<Type> discovered = DiscoverConsumers();

        // Assert
        foreach (Type consumerType in discovered)
        {
            await Assert.That(consumerType.IsClass).IsTrue();
        }
    }
}
