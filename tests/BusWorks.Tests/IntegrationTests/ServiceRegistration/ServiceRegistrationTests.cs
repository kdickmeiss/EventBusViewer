using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions;
using BusWorks.Options;
using BusWorks.Publisher;
using BusWorks.Tests.IntegrationTests.BuildingBlocks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;

namespace BusWorks.Tests.IntegrationTests.ServiceRegistration;

/// <summary>
/// Integration tests that verify all EventBus services are correctly wired up in the DI container.
/// </summary>
public sealed class ServiceRegistrationTests : TestBase
{
    public ServiceRegistrationTests(EventBusHostFactory factory) : base(factory) { }

    [Fact]
    public void IEventBusPublisher_IsRegistered_AndResolvesSuccessfully()
    {
        IEventBusPublisher publisher = GetRequiredService<IEventBusPublisher>();

        publisher.ShouldNotBeNull();
    }

    [Fact]
    public void IEventBusPublisher_ResolvesToServiceBusPublisher()
    {
        IEventBusPublisher publisher = GetRequiredService<IEventBusPublisher>();

        publisher.ShouldBeOfType<ServiceBusPublisher>();
    }

    [Fact]
    public void IEventBusPublisher_IsRegisteredAsSingleton_ReturnsSameInstanceOnEveryResolve()
    {
        IEventBusPublisher first = GetRequiredService<IEventBusPublisher>();
        IEventBusPublisher second = GetRequiredService<IEventBusPublisher>();

        second.ShouldBeSameAs(first);
    }

    [Fact]
    public void ServiceBusClient_IsRegistered_AndResolvesSuccessfully()
    {
        ServiceBusClient client = GetRequiredService<ServiceBusClient>();

        client.ShouldNotBeNull();
    }

    [Fact]
    public void ServiceBusClient_IsRegisteredAsSingleton_ReturnsSameInstanceOnEveryResolve()
    {
        ServiceBusClient first = GetRequiredService<ServiceBusClient>();
        ServiceBusClient second = GetRequiredService<ServiceBusClient>();

        second.ShouldBeSameAs(first);
    }

    [Fact]
    public void ServiceBusAssemblyRegistry_IsRegistered_AndResolvesSuccessfully()
    {
        ServiceBusAssemblyRegistry registry = GetRequiredService<ServiceBusAssemblyRegistry>();

        registry.ShouldNotBeNull();
    }

    [Fact]
    public void Tracer_IsRegistered_AndResolvesSuccessfully()
    {
        Tracer tracer = GetRequiredService<Tracer>();

        tracer.ShouldNotBeNull();
    }

    [Fact]
    public void EventBusOptions_AuthenticationType_BindsFromConfiguration()
    {
        IOptions<BusWorksOptions> options = GetRequiredService<IOptions<BusWorksOptions>>();

        options.Value.AuthenticationType.ShouldBe(EventBusAuthenticationType.ConnectionString);
    }

    [Fact]
    public void EventBusOptions_ConnectionString_BindsFromConfiguration()
    {
        IOptions<BusWorksOptions> options = GetRequiredService<IOptions<BusWorksOptions>>();

        options.Value.ConnectionString?.ConnectionString.ShouldBe(Emulator.ConnectionString);
    }

    [Fact]
    public void EventBusOptions_MaxConcurrentCalls_BindsFromConfiguration()
    {
        IOptions<BusWorksOptions> options = GetRequiredService<IOptions<BusWorksOptions>>();

        options.Value.MaxConcurrentCalls.ShouldBe(10);
    }

    [Fact]
    public void EventBusOptions_MaxConcurrentSessions_BindsFromConfiguration()
    {
        IOptions<BusWorksOptions> options = GetRequiredService<IOptions<BusWorksOptions>>();

        options.Value.MaxConcurrentSessions.ShouldBe(8);
    }

    [Fact]
    public void EventBusOptions_MaxConcurrentCallsPerSession_BindsFromConfiguration()
    {
        IOptions<BusWorksOptions> options = GetRequiredService<IOptions<BusWorksOptions>>();

        options.Value.MaxConcurrentCallsPerSession.ShouldBe(1);
    }
}
