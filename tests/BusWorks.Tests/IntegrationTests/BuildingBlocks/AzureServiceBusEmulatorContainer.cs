using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Testcontainers.MsSql;
using Testcontainers.ServiceBus;

namespace BusWorks.Tests.IntegrationTests.BuildingBlocks;

/// <summary>
/// Manages the Docker lifecycle of the Azure Service Bus emulator container.
/// </summary>
/// <remarks>
/// Responsible only for starting the container, wiring up the AMQP
/// <see cref="ServiceBusClient"/> and the HTTP <see cref="ServiceBusAdministrationClient"/>.
/// Entity provisioning (queues, topics, subscriptions) is intentionally <b>not</b> done here —
/// each test class declares the entities it needs by overriding <c>EventBusTestBase.ProvisionEntitiesAsync</c>.
/// </remarks>
public sealed class AzureServiceBusEmulatorContainer{
    /// <summary>AMQP port used by <see cref="ServiceBusClient"/> for messaging.</summary>
    private const ushort AmqpPort = 5672;

    /// <summary>HTTP port used by the emulator's management REST API and health endpoint.</summary>
    private const ushort HttpPort = 5300;

    private readonly ServiceBusContainer _container;

    /// <summary>
    /// Connection string for the AMQP endpoint.
    /// Use this to construct a <see cref="ServiceBusClient"/> for send / receive operations.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// A ready-to-use <see cref="ServiceBusClient"/> connected to the emulator.
    /// Shared across all callers — do <b>not</b> dispose it from test code.
    /// </summary>
    public ServiceBusClient Client { get; private set; } = null!;

    /// <summary>
    /// Administration client for creating and inspecting Service Bus entities.
    /// Test classes obtain this via the <c>EventBusTestBase</c> helper methods —
    /// call it directly only when you need low-level control over entity options.
    /// </summary>
    public ServiceBusAdministrationClient AdminClient { get; private set; } = null!;

    public AzureServiceBusEmulatorContainer()
    {
        // 1. Create a network for the SQL Edge container
        INetwork? network = new NetworkBuilder().Build();
        
        // 2. Create the SQL Edge container
        MsSqlContainer? sqlContainer = new MsSqlBuilder("mcr.microsoft.com/azure-sql-edge:latest")
            .WithNetwork(network)
            .WithNetworkAliases("sql-edge")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", "Your_password123")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1433))
            .Build();
        
        // 3. Create the Service Bus emulator container, linking to the SQL Edge container
        _container = new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:latest")
            .WithMsSqlContainer(network, sqlContainer, "sql-edge", "Your_password123")
            .WithAcceptLicenseAgreement(true)
            .Build();
        
        // _container = new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:latest")
        //     .WithAcceptLicenseAgreement(true)
        //     // Bind both the AMQP and the HTTP management ports to random host ports so
        //     // that parallel test runs never collide on a fixed port number.
        //     .WithPortBinding(AmqpPort, assignRandomHostPort: true)
        //     .WithPortBinding(HttpPort, assignRandomHostPort: true)
        //     // Reduce SQL startup noise — the emulator uses an internal SQL instance.
        //     .WithEnvironment("SQL_WAIT_INTERVAL", "0")
        //     // Wait until the HTTP health endpoint confirms the emulator is ready before
        //     // returning control to InitializeAsync.
        //     .WithWaitStrategy(
        //         Wait.ForUnixContainer()
        //             .UntilHttpRequestIsSucceeded(r =>
        //                 r.ForPort(HttpPort).ForPath("/health")))
        //     .Build();
    }
    
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Build the AMQP connection string (for ServiceBusClient).
        ConnectionString = _container.GetConnectionString();

        // Build the HTTP management connection string (for ServiceBusAdministrationClient).
        // The admin client communicates over HTTP REST — the emulator exposes this on the
        // HTTP port (5300), NOT the AMQP port (5672), so we substitute the port number.
        ushort adminPort = _container.GetMappedPublicPort(HttpPort);
        string adminConnectionString =
            $"Endpoint=sb://localhost:{adminPort}/;" +
            "SharedAccessKeyName=RootManageSharedAccessKey;" +
            "SharedAccessKey=SASKEY=;" +
            "UseDevelopmentEmulator=true;";

        Client = new ServiceBusClient(ConnectionString);
        AdminClient = new ServiceBusAdministrationClient(adminConnectionString);
    }
    
    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _container.DisposeAsync();
    }
}


