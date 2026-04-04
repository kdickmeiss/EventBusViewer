using BusWorks.Abstractions.Consumer;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;
using Xunit;

namespace BusWorks.Tests.IntegrationTests.ConsumerDiscovery;

/// <summary>
/// Integration tests that verify <see cref="ServiceBusAssemblyRegistry"/> drives consumer
/// discovery correctly — using the exact same scanning predicate that
/// <c>ServiceBusProcessorBackgroundService</c> runs at application startup.
/// </summary>
public sealed partial class ConsumerDiscoveryTests : TestBase
{
    public ConsumerDiscoveryTests(EventBusHostFactory factory) : base(factory) { }

    /// <summary>
    /// Delegates directly to <see cref="ServiceBusAssemblyRegistry.GetConsumerTypes"/>, 
    /// which mirrors what the background service calls at startup.
    /// </summary>
    // S2325: False positive — GetRequiredService() accesses the Services instance property.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S2325", Justification = "Accesses instance data via GetRequiredService -> Services")]
    private IReadOnlyList<Type> DiscoverConsumers()
    {
        ServiceBusAssemblyRegistry registry = GetRequiredService<ServiceBusAssemblyRegistry>();
        return registry.GetConsumerTypes();
    }

    [Fact]
    public void ConcreteConsumer_InTestAssembly_IsDiscovered()
    {
        // Act
        IReadOnlyList<Type> discovered = DiscoverConsumers();

        // Assert
        Assert.Contains(typeof(ConcreteIntegrationConsumer), discovered);
    }

    [Fact]
    public void AbstractConsumer_IsExcludedFromDiscovery()
    {
        // Abstract types cannot be instantiated — they must never reach the processor setup.

        // Act
        IReadOnlyList<Type> discovered = DiscoverConsumers();

        // Assert
        Assert.DoesNotContain(typeof(AbstractIntegrationConsumer), discovered);
    }

    [Fact]
    public void NonConsumerClass_IsExcludedFromDiscovery()
    {
        // A plain class that does not implement IConsumer<T> must be excluded.

        // Act
        IReadOnlyList<Type> discovered = DiscoverConsumers();

        // Assert
        Assert.DoesNotContain(typeof(NotAConsumer), discovered);
    }

    [Fact]
    public void AllDiscoveredConsumers_ImplementIConsumerOfT()
    {
        // Every discovered type must implement the IConsumer<T> contract so the
        // background service can resolve and invoke them safely.

        // Act
        IReadOnlyList<Type> discovered = DiscoverConsumers();

        // Assert
        foreach (Type consumerType in discovered)
        {
            bool implementsIConsumer = consumerType
                .GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>));

            Assert.True(implementsIConsumer);
        }
    }

    [Fact]
    public void AllDiscoveredConsumers_AreConcrete_NotAbstract()
    {
        // The background service resolves consumers from DI — abstract types would
        // throw at resolution time, so they must be filtered out at the scanning stage.

        // Act
        IReadOnlyList<Type> discovered = DiscoverConsumers();

        // Assert
        foreach (Type consumerType in discovered)
        {
            Assert.False(consumerType.IsAbstract);
        }
    }

    [Fact]
    public void AllDiscoveredConsumers_AreClasses_NotInterfacesOrValueTypes()
    {
        // Act
        IReadOnlyList<Type> discovered = DiscoverConsumers();

        // Assert
        foreach (Type consumerType in discovered)
        {
            Assert.True(consumerType.IsClass);
        }
    }
}
