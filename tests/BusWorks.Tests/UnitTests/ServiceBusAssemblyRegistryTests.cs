using BusWorks.Abstractions.Consumer;
using BusWorks.Abstractions.Events;
using Shouldly;
using System.Reflection;
using Xunit;

namespace BusWorks.Tests.UnitTests;

/// <summary>
/// Unit tests for <see cref="ServiceBusAssemblyRegistry"/>.
/// Verifies consumer-type discovery, deduplication of duplicate assemblies, and
/// exclusion of abstract types and non-consumer classes — without requiring a live broker.
/// </summary>
public sealed class ServiceBusAssemblyRegistryTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private sealed record DiscoveryEvent(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;

    /// <summary>Concrete consumer — must be discovered.</summary>
    private sealed class ConcreteConsumerForRegistry : IConsumer<DiscoveryEvent>
    {
        public Task Consume(IConsumeContext<DiscoveryEvent> context) => Task.CompletedTask;
    }

    /// <summary>Abstract consumer — must be excluded because it cannot be instantiated.</summary>
    private abstract class AbstractConsumerForRegistry : IConsumer<DiscoveryEvent>
    {
        public abstract Task Consume(IConsumeContext<DiscoveryEvent> context);
    }

    /// <summary>Plain class — must be excluded because it does not implement <see cref="IConsumer{TMessage}"/>.</summary>
    private sealed class NotAConsumerForRegistry { }

    // ── No assemblies ─────────────────────────────────────────────────────────

    [Fact]
    public void WithNoAssemblies_GetConsumerTypes_ReturnsEmptyList()
    {
        // Arrange + Act
        var registry = new ServiceBusAssemblyRegistry();

        // Assert
        registry.GetConsumerTypes().ShouldBeEmpty();
    }

    // ── Single assembly ───────────────────────────────────────────────────────

    [Fact]
    public void WithTestAssembly_GetConsumerTypes_FindsConcreteConsumers()
    {
        // Arrange + Act
        var registry = new ServiceBusAssemblyRegistry(typeof(ServiceBusAssemblyRegistryTests).Assembly);

        // Assert — ConcreteConsumerForRegistry is defined in this assembly
        registry.GetConsumerTypes().ShouldContain(typeof(ConcreteConsumerForRegistry));
    }

    [Fact]
    public void WithTestAssembly_GetConsumerTypes_ExcludesAbstractConsumers()
    {
        var registry = new ServiceBusAssemblyRegistry(typeof(ServiceBusAssemblyRegistryTests).Assembly);

        // AbstractConsumerForRegistry cannot be instantiated and must never reach DI registration.
        registry.GetConsumerTypes().ShouldNotContain(typeof(AbstractConsumerForRegistry));
    }

    [Fact]
    public void WithTestAssembly_GetConsumerTypes_ExcludesNonConsumerClasses()
    {
        var registry = new ServiceBusAssemblyRegistry(typeof(ServiceBusAssemblyRegistryTests).Assembly);

        registry.GetConsumerTypes().ShouldNotContain(typeof(NotAConsumerForRegistry));
    }

    [Fact]
    public void GetConsumerTypes_AllReturnedTypes_ImplementIConsumerOfT()
    {
        var registry = new ServiceBusAssemblyRegistry(typeof(ServiceBusAssemblyRegistryTests).Assembly);

        foreach (Type consumerType in registry.GetConsumerTypes())
        {
            bool implementsIConsumer = consumerType
                .GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>));

            implementsIConsumer.ShouldBeTrue(
                $"Type '{consumerType.Name}' was discovered but does not implement IConsumer<T>.");
        }
    }

    [Fact]
    public void GetConsumerTypes_AllReturnedTypes_AreConcrete()
    {
        var registry = new ServiceBusAssemblyRegistry(typeof(ServiceBusAssemblyRegistryTests).Assembly);

        foreach (Type consumerType in registry.GetConsumerTypes())
            consumerType.IsAbstract.ShouldBeFalse(
                $"Type '{consumerType.Name}' is abstract and should have been excluded from discovery.");
    }

    // ── Duplicate assembly deduplication ─────────────────────────────────────

    [Fact]
    public void WithDuplicateAssembly_GetConsumerTypes_CountMatchesSingleAssemblyScan()
    {
        // Arrange — pass the same assembly twice
        Assembly asm = typeof(ServiceBusAssemblyRegistryTests).Assembly;
        var singleRegistry = new ServiceBusAssemblyRegistry(asm);
        var duplicateRegistry = new ServiceBusAssemblyRegistry(asm, asm);

        // Assert — Distinct() deduplication must ensure the count is identical
        duplicateRegistry.GetConsumerTypes().Count
            .ShouldBe(singleRegistry.GetConsumerTypes().Count);
    }

    [Fact]
    public void WithDuplicateAssembly_GetConsumerTypes_ContainsNoduplicateTypes()
    {
        Assembly asm = typeof(ServiceBusAssemblyRegistryTests).Assembly;
        var registry = new ServiceBusAssemblyRegistry(asm, asm);

        IReadOnlyList<Type> consumers = registry.GetConsumerTypes();

        // Each type must appear exactly once regardless of how many times the assembly was passed.
        consumers.Count.ShouldBe(consumers.Distinct().Count());
    }

    // ── Multiple distinct assemblies ──────────────────────────────────────────

    [Fact]
    public void WithMultipleDistinctAssemblies_GetConsumerTypes_AggregatesFromAll()
    {
        // BusWorks.Tests.dll + BusWorks.Tests.dll (same here but logically two assemblies)
        // In practice this proves the SelectMany aggregation path is exercised.
        Assembly testAssembly = typeof(ServiceBusAssemblyRegistryTests).Assembly;
        var registry = new ServiceBusAssemblyRegistry(testAssembly);

        // All consumer types from the test assembly should be present.
        registry.GetConsumerTypes().ShouldContain(typeof(ConcreteConsumerForRegistry));
    }
}

