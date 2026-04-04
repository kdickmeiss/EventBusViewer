using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions;
using BusWorks.Options;
using BusWorks.Publisher;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using Xunit;

namespace BusWorks.Tests.IntegrationTests.ServiceRegistration;

/// <summary>
/// Integration tests that verify all EventBus services are correctly wired up in the DI
/// container.
/// </summary>
/// <remarks>
/// A single <see cref="EventBusHostFactory"/> is shared for the entire test collection
/// via <see cref="ICollectionFixture{TFixture}"/>, mirroring TUnit's
/// <c>SharedType.PerTestSession</c> pattern. The host is built once; all tests resolve
/// services from the same container.
/// </remarks>
public sealed class ServiceRegistrationTests : TestBase
{
    public ServiceRegistrationTests(EventBusHostFactory factory) : base(factory) { }

    [Fact]
    public void IEventBusPublisher_IsRegistered_AndResolvesSuccessfully()
    {
        IEventBusPublisher publisher = GetRequiredService<IEventBusPublisher>();

        Assert.NotNull(publisher);
    }

    [Fact]
    public void IEventBusPublisher_ResolvesToServiceBusPublisher()
    {
        // ServiceBusPublisher is internal — visible here via InternalsVisibleTo.
        IEventBusPublisher publisher = GetRequiredService<IEventBusPublisher>();

        Assert.IsType<ServiceBusPublisher>(publisher);
    }

    [Fact]
    public void IEventBusPublisher_IsRegisteredAsSingleton_ReturnsSameInstanceOnEveryResolve()
    {
        IEventBusPublisher first = GetRequiredService<IEventBusPublisher>();
        IEventBusPublisher second = GetRequiredService<IEventBusPublisher>();

        Assert.Same(first, second);
    }

    [Fact]
    public void ServiceBusClient_IsRegistered_AndResolvesSuccessfully()
    {
        ServiceBusClient client = GetRequiredService<ServiceBusClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void ServiceBusClient_IsRegisteredAsSingleton_ReturnsSameInstanceOnEveryResolve()
    {
        ServiceBusClient first = GetRequiredService<ServiceBusClient>();
        ServiceBusClient second = GetRequiredService<ServiceBusClient>();

        Assert.Same(first, second);
    }

    [Fact]
    public void ServiceBusAssemblyRegistry_IsRegistered_AndResolvesSuccessfully()
    {
        ServiceBusAssemblyRegistry registry = GetRequiredService<ServiceBusAssemblyRegistry>();

        Assert.NotNull(registry);
    }

    [Fact]
    public void Tracer_IsRegistered_AndResolvesSuccessfully()
    {
        Tracer tracer = GetRequiredService<Tracer>();

        Assert.NotNull(tracer);
    }

    [Fact]
    public void EventBusOptions_AuthenticationType_BindsFromConfiguration()
    {
        IOptions<BusWorksOptions> options = GetRequiredService<IOptions<BusWorksOptions>>();

        Assert.Equal(EventBusAuthenticationType.ConnectionString, options.Value.AuthenticationType);
    }

    [Fact]
    public void EventBusOptions_ConnectionString_BindsFromConfiguration()
    {
        IOptions<BusWorksOptions> options = GetRequiredService<IOptions<BusWorksOptions>>();

        Assert.Equal(Emulator.ConnectionString, options.Value.ConnectionString?.ConnectionString);
    }

    [Fact]
    public void EventBusOptions_MaxConcurrentCalls_BindsFromConfiguration()
    {
        IOptions<BusWorksOptions> options = GetRequiredService<IOptions<BusWorksOptions>>();

        Assert.Equal(10, options.Value.MaxConcurrentCalls);
    }

    [Fact]
    public void EventBusOptions_MaxConcurrentSessions_BindsFromConfiguration()
    {
        IOptions<BusWorksOptions> options = GetRequiredService<IOptions<BusWorksOptions>>();

        Assert.Equal(8, options.Value.MaxConcurrentSessions);
    }

    [Fact]
    public void EventBusOptions_MaxConcurrentCallsPerSession_BindsFromConfiguration()
    {
        IOptions<BusWorksOptions> options = GetRequiredService<IOptions<BusWorksOptions>>();

        Assert.Equal(1, options.Value.MaxConcurrentCallsPerSession);
    }
}
