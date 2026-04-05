using System.Reflection;
using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions;
using BusWorks.Options;
using BusWorks.Publisher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;

namespace BusWorks.Tests.UnitTests.DependencyInjection;

/// <summary>
/// Unit tests for <see cref="BusWorks.DependencyInjection"/>.
/// No real Service Bus connection is needed — <see cref="ServiceBusClient"/> defers
/// all I/O until the first send/receive, so construction with a fake connection string is safe.
/// <para>
/// <see cref="ServiceBusPublisher"/> requires a <see cref="Tracer"/> that the host application
/// is expected to provide. Tests that resolve <see cref="IEventBusPublisher"/> register
/// a no-op tracer to satisfy that dependency.
/// </para>
/// </summary>
public sealed class DependencyInjectionTests
{
    // A syntactically valid connection string that satisfies ServiceBusClient's constructor
    // without requiring a live broker.
    private const string FakeConnectionString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=abc123abc123abc123abc123abc123abc123abc123abc=";

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static IConfiguration BuildConnectionStringConfig(string connectionString = FakeConnectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{BusWorksOptions.SectionName}:AuthenticationType"] =
                    nameof(EventBusAuthenticationType.ConnectionString),
                [$"{BusWorksOptions.SectionName}:ConnectionString:ConnectionString"] = connectionString
            })
            .Build();

    private static BusWorksOptions ValidConnectionStringOptions() => new()
    {
        AuthenticationType = EventBusAuthenticationType.ConnectionString,
        ConnectionString = new ConnectionStringOptions { ConnectionString = FakeConnectionString }
    };

    /// <summary>
    /// Registers a no-op <see cref="Tracer"/> so that <see cref="ServiceBusPublisher"/>
    /// can be resolved without a real OpenTelemetry pipeline.
    /// </summary>
    private static void AddNoOpTracer(IServiceCollection services) =>
        services.AddSingleton(TracerProvider.Default.GetTracer("BusWorks.Tests"));

    // ── IConfiguration overload ──────────────────────────────────────────────

    [Fact]
    public async Task AddBusWorks_WithValidConfiguration_RegistersIEventBusPublisher()
    {
        ServiceCollection services = new();
        services.AddBusWorks(BuildConnectionStringConfig());
        AddNoOpTracer(services);

        await using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEventBusPublisher>().ShouldNotBeNull();
    }

    [Fact]
    public async Task AddBusWorks_WithValidConfiguration_IEventBusPublisher_ResolvesToServiceBusPublisher()
    {
        ServiceCollection services = new();
        services.AddBusWorks(BuildConnectionStringConfig());
        AddNoOpTracer(services);

        await using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEventBusPublisher>().ShouldBeOfType<ServiceBusPublisher>();
    }

    [Fact]
    public void AddBusWorks_WithValidConfiguration_RegistersServiceBusClient()
    {
        ServiceCollection services = new();
        services.AddBusWorks(BuildConnectionStringConfig());

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<ServiceBusClient>().ShouldNotBeNull();
    }

    [Fact]
    public void AddBusWorks_WithValidConfiguration_RegistersServiceBusAssemblyRegistry()
    {
        ServiceCollection services = new();
        services.AddBusWorks(BuildConnectionStringConfig());

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<ServiceBusAssemblyRegistry>().ShouldNotBeNull();
    }

    [Fact]
    public void AddBusWorks_WithValidConfiguration_BindsAuthenticationTypeFromConfig()
    {
        ServiceCollection services = new();
        services.AddBusWorks(BuildConnectionStringConfig());

        using ServiceProvider provider = services.BuildServiceProvider();
        IOptions<BusWorksOptions> options = provider.GetRequiredService<IOptions<BusWorksOptions>>();

        options.Value.AuthenticationType.ShouldBe(EventBusAuthenticationType.ConnectionString);
    }

    [Fact]
    public void AddBusWorks_WithValidConfiguration_BindsConnectionStringFromConfig()
    {
        ServiceCollection services = new();
        services.AddBusWorks(BuildConnectionStringConfig());

        using ServiceProvider provider = services.BuildServiceProvider();
        IOptions<BusWorksOptions> options = provider.GetRequiredService<IOptions<BusWorksOptions>>();

        options.Value.ConnectionString?.ConnectionString.ShouldBe(FakeConnectionString);
    }

    [Fact]
    public void AddBusWorks_WithMissingConfigSection_ThrowsInvalidOperationException()
    {
        ServiceCollection services = new();
        IConfiguration empty = new ConfigurationBuilder().Build();

        Should.Throw<InvalidOperationException>(() => services.AddBusWorks(empty));
    }

    // ── BusWorksOptions overload ─────────────────────────────────────────────

    [Fact]
    public void AddBusWorks_WithBusWorksOptions_RegistersMatchingIOptions()
    {
        ServiceCollection services = new();
        BusWorksOptions options = ValidConnectionStringOptions();
        services.AddBusWorks(options);

        using ServiceProvider provider = services.BuildServiceProvider();
        IOptions<BusWorksOptions> resolved = provider.GetRequiredService<IOptions<BusWorksOptions>>();

        resolved.Value.ShouldBeSameAs(options);
    }

    [Fact]
    public async Task AddBusWorks_WithBusWorksOptions_RegistersIEventBusPublisher()
    {
        ServiceCollection services = new();
        services.AddBusWorks(ValidConnectionStringOptions());
        AddNoOpTracer(services);

        await using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEventBusPublisher>().ShouldNotBeNull();
    }

    [Fact]
    public void AddBusWorks_WithBusWorksOptions_RegistersServiceBusClient()
    {
        ServiceCollection services = new();
        services.AddBusWorks(ValidConnectionStringOptions());

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<ServiceBusClient>().ShouldNotBeNull();
    }

    [Fact]
    public void AddBusWorks_WithBusWorksOptions_RegistersServiceBusAssemblyRegistry()
    {
        ServiceCollection services = new();
        services.AddBusWorks(ValidConnectionStringOptions());

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<ServiceBusAssemblyRegistry>().ShouldNotBeNull();
    }

    // ── Action<BusWorksOptions> overload ─────────────────────────────────────

    [Fact]
    public void AddBusWorks_WithActionDelegate_AppliesConfigurationToOptions()
    {
        ServiceCollection services = new();
        services.AddBusWorks(o =>
        {
            o.AuthenticationType = EventBusAuthenticationType.ConnectionString;
            o.ConnectionString = new ConnectionStringOptions { ConnectionString = FakeConnectionString };
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        IOptions<BusWorksOptions> options = provider.GetRequiredService<IOptions<BusWorksOptions>>();

        options.Value.AuthenticationType.ShouldBe(EventBusAuthenticationType.ConnectionString);
        options.Value.ConnectionString?.ConnectionString.ShouldBe(FakeConnectionString);
    }

    [Fact]
    public async Task AddBusWorks_WithActionDelegate_RegistersIEventBusPublisher()
    {
        ServiceCollection services = new();
        services.AddBusWorks(o =>
        {
            o.AuthenticationType = EventBusAuthenticationType.ConnectionString;
            o.ConnectionString = new ConnectionStringOptions { ConnectionString = FakeConnectionString };
        });
        AddNoOpTracer(services);

        await using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEventBusPublisher>().ShouldNotBeNull();
    }

    // ── GetServiceBusClientByConfig guard clauses ────────────────────────────

    [Fact]
    public void AddBusWorks_ConnectionStringAuth_WithNullConnectionStringOptions_Throws()
    {
        ServiceCollection services = new();
        BusWorksOptions options = new()
        {
            AuthenticationType = EventBusAuthenticationType.ConnectionString,
            ConnectionString = null
        };

        Should.Throw<InvalidOperationException>(() => services.AddBusWorks(options));
    }

    [Fact]
    public void AddBusWorks_ManagedIdentityAuth_WithNullManagedIdentityOptions_Throws()
    {
        ServiceCollection services = new();
        BusWorksOptions options = new()
        {
            AuthenticationType = EventBusAuthenticationType.ManagedIdentity,
            ManagedIdentity = null
        };

        Should.Throw<InvalidOperationException>(() => services.AddBusWorks(options));
    }

    [Fact]
    public void AddBusWorks_AzureCliAuth_WithNullAzureCliOptions_Throws()
    {
        ServiceCollection services = new();
        BusWorksOptions options = new()
        {
            AuthenticationType = EventBusAuthenticationType.AzureCli,
            AzureCli = null
        };

        Should.Throw<InvalidOperationException>(() => services.AddBusWorks(options));
    }

    [Fact]
    public void AddBusWorks_ApplicationRegistrationAuth_WithNullRegistrationOptions_Throws()
    {
        ServiceCollection services = new();
        BusWorksOptions options = new()
        {
            AuthenticationType = EventBusAuthenticationType.ApplicationRegistration,
            ApplicationRegistration = null
        };

        Should.Throw<InvalidOperationException>(() => services.AddBusWorks(options));
    }

    [Fact]
    public void AddBusWorks_UnknownAuthenticationType_Throws()
    {
        ServiceCollection services = new();
        BusWorksOptions options = new()
        {
            AuthenticationType = (EventBusAuthenticationType)99,
            ConnectionString = new ConnectionStringOptions { ConnectionString = FakeConnectionString }
        };

        Should.Throw<InvalidOperationException>(() => services.AddBusWorks(options));
    }

    // ── Singleton lifetime verification ──────────────────────────────────────

    [Fact]
    public async Task AddBusWorks_IEventBusPublisher_IsRegisteredAsSingleton()
    {
        ServiceCollection services = new();
        services.AddBusWorks(ValidConnectionStringOptions());
        AddNoOpTracer(services);

        await using ServiceProvider provider = services.BuildServiceProvider();
        IEventBusPublisher first = provider.GetRequiredService<IEventBusPublisher>();
        IEventBusPublisher second = provider.GetRequiredService<IEventBusPublisher>();

        second.ShouldBeSameAs(first);
    }

    [Fact]
    public void AddBusWorks_ServiceBusClient_IsRegisteredAsSingleton()
    {
        ServiceCollection services = new();
        services.AddBusWorks(ValidConnectionStringOptions());

        using ServiceProvider provider = services.BuildServiceProvider();
        ServiceBusClient first = provider.GetRequiredService<ServiceBusClient>();
        ServiceBusClient second = provider.GetRequiredService<ServiceBusClient>();

        second.ShouldBeSameAs(first);
    }

    // ── Consumer registration ─────────────────────────────────────────────────

    /// <summary>
    /// Every consumer type discovered from the supplied assemblies must be registered
    /// with <see cref="ServiceLifetime.Scoped"/> so that
    /// <c>ServiceBusMessageProcessorBuilder.Build()</c> can resolve them from a DI scope.
    /// </summary>
    [Fact]
    public void AddBusWorks_WithConsumerAssemblies_RegistersEachConsumerTypeAsScoped()
    {
        ServiceCollection services = new();
        Assembly testAssembly = typeof(DependencyInjectionTests).Assembly;
        services.AddBusWorks(ValidConnectionStringOptions(), testAssembly);

        // Derive the same consumer list that AddBusWorks scanned internally.
        var registry = new ServiceBusAssemblyRegistry(testAssembly);
        IReadOnlyList<Type> consumerTypes = registry.GetConsumerTypes();

        // The test assembly contains several consumer fixtures — at least one must be present.
        consumerTypes.ShouldNotBeEmpty("the test assembly must contain at least one IConsumer<T>");

        foreach (Type consumerType in consumerTypes)
        {
            ServiceDescriptor? descriptor = services.FirstOrDefault(sd => sd.ServiceType == consumerType);
            descriptor.ShouldNotBeNull($"Consumer '{consumerType.Name}' should be registered in the service collection.");
            descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped, $"Consumer '{consumerType.Name}' must be registered as Scoped.");
        }
    }
}


