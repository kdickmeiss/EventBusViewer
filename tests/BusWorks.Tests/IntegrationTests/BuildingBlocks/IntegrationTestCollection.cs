using Xunit;

namespace BusWorks.Tests.IntegrationTests.BuildingBlocks;

/// <summary>
/// Defines the xUnit collection that shares a single <see cref="EventBusHostFactory"/> instance
/// across every integration-test class in the suite.
/// <para>
/// All test classes that derive from <see cref="TestBase"/> automatically belong to this
/// collection because the <c>[Collection]</c> attribute is declared on the base class.
/// Tests within a collection are executed sequentially, which replaces TUnit's
/// <c>[NotInParallel]</c> attribute.
/// </para>
/// </summary>
[CollectionDefinition(nameof(IntegrationTestCollection))]
public class IntegrationTestCollection : ICollectionFixture<EventBusHostFactory>;

