using System.Reflection;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions;
using BusWorks.Abstractions.Events;
using BusWorks.BackgroundServices;
using BusWorks.Options;
using BusWorks.Publisher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BusWorks;

public static class DependencyInjection
{
    /// <summary>
    /// Registers BusWorks services using options bound from <see cref="IConfiguration"/>.
    /// </summary>
    /// <remarks>
    /// Options are read from the <c>EventBusOptions</c> configuration section (e.g. <c>appsettings.json</c>).
    /// Changes to configuration at runtime are automatically picked up via the Options pattern.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configuration">The application configuration containing the <c>EventBusOptions</c> section.</param>
    /// <param name="consumerAssemblies">
    /// One or more assemblies to scan for <see cref="IIntegrationEvent"/> consumer implementations.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the <c>EventBusOptions</c> configuration section is missing or empty.
    /// </exception>
    public static IServiceCollection AddBusWorks(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] consumerAssemblies)
    {
        BusWorksOptions worksOptions = configuration
                                           .GetSection(BusWorksOptions.SectionName)
                                           .Get<BusWorksOptions>()
                                       ?? throw new InvalidOperationException(
                                           $"The '{BusWorksOptions.SectionName}' configuration section is missing or empty.");

        services.Configure<BusWorksOptions>(configuration.GetSection(BusWorksOptions.SectionName));

        return services.AddBusWorksCore(worksOptions, consumerAssemblies);
    }

    /// <summary>
    /// Registers BusWorks services using a pre-built <see cref="BusWorksOptions"/> instance.
    /// </summary>
    /// <remarks>
    /// Use this overload when options are constructed programmatically or in test scenarios.
    /// The provided <paramref name="worksOptions"/> instance is registered as a singleton
    /// and made available via <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="worksOptions">The fully configured <see cref="BusWorksOptions"/> instance.</param>
    /// <param name="consumerAssemblies">
    /// One or more assemblies to scan for <see cref="IIntegrationEvent"/> consumer implementations.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddBusWorks(
        this IServiceCollection services,
        BusWorksOptions worksOptions,
        params Assembly[] consumerAssemblies)
    {
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(worksOptions));

        return services.AddBusWorksCore(worksOptions, consumerAssemblies);
    }

    /// <summary>
    /// Registers BusWorks services using a configuration delegate.
    /// </summary>
    /// <remarks>
    /// This is the most idiomatic .NET approach, allowing options to be configured inline with code:
    /// <code>
    /// services.AddBusWorks(o =>
    /// {
    ///     o.AuthenticationType = EventBusAuthenticationType.AzureCli;
    ///     o.AzureCli = new AzureCliOptions { FullyQualifiedNamespace = "my-ns.servicebus.windows.net" };
    /// }, typeof(MyConsumer).Assembly);
    /// </code>
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configure">A delegate that configures the <see cref="BusWorksOptions"/>.</param>
    /// <param name="consumerAssemblies">
    /// One or more assemblies to scan for <see cref="IIntegrationEvent"/> consumer implementations.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddBusWorks(
        this IServiceCollection services,
        Action<BusWorksOptions> configure,
        params Assembly[] consumerAssemblies)
    {
        var options = new BusWorksOptions();
        configure(options);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        return services.AddBusWorks(options, consumerAssemblies);
    }

    /// <summary>
    /// Core registration logic shared by all public <c>AddBusWorks</c> overloads.
    /// Builds the <see cref="ServiceBusClient"/>, registers consumer types discovered from
    /// <paramref name="consumerAssemblies"/>, and wires up all required BusWorks services.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="worksOptions">The resolved <see cref="BusWorksOptions"/>.</param>
    /// <param name="consumerAssemblies">Assemblies to scan for consumer implementations.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    private static IServiceCollection AddBusWorksCore(
        this IServiceCollection services,
        BusWorksOptions worksOptions,
        params Assembly[] consumerAssemblies)
    {
        ServiceBusClient serviceBusClient = GetServiceBusClientByConfig(worksOptions);

        var registry = new ServiceBusAssemblyRegistry(consumerAssemblies);

        foreach (Type consumerType in registry.GetConsumerTypes())
            services.AddScoped(consumerType);

        services
            .AddSingleton(serviceBusClient)
            .AddSingleton(registry)
            .AddSingleton<IEventBusPublisher, ServiceBusPublisher>();

        services.AddHostedService<ServiceBusProcessorBackgroundService>();

        return services;
    }


    /// <summary>
    /// Creates and returns a <see cref="ServiceBusClient"/> configured according to the
    /// <see cref="BusWorksOptions.AuthenticationType"/> specified in <paramref name="worksOptions"/>.
    /// </summary>
    /// <param name="worksOptions">The <see cref="BusWorksOptions"/> containing authentication configuration.</param>
    /// <returns>A fully configured <see cref="ServiceBusClient"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a required configuration value for the selected authentication type is missing,
    /// or when an unsupported <see cref="EventBusAuthenticationType"/> value is specified.
    /// </exception>
    private static ServiceBusClient GetServiceBusClientByConfig(BusWorksOptions worksOptions) =>
        worksOptions.AuthenticationType switch
        {
            EventBusAuthenticationType.ConnectionString =>
                new ServiceBusClient(
                    worksOptions.ConnectionString?.ConnectionString
                    ?? throw new InvalidOperationException(
                        $"EventBusOptions.ConnectionString.ConnectionString is required when AuthenticationType is '{nameof(EventBusAuthenticationType.ConnectionString)}'")),

            EventBusAuthenticationType.ManagedIdentity =>
                new ServiceBusClient(
                    worksOptions.ManagedIdentity?.FullyQualifiedNamespace
                    ?? throw new InvalidOperationException(
                        $"EventBusOptions.ManagedIdentity.FullyQualifiedNamespace is required when AuthenticationType is '{nameof(EventBusAuthenticationType.ManagedIdentity)}'"),
                    worksOptions.ManagedIdentity.ClientId is { Length: > 0 } clientId
                        ? new ManagedIdentityCredential(
                            ManagedIdentityId.FromUserAssignedClientId(clientId)) // user-assigned
                        : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)), // system-assigned

            EventBusAuthenticationType.AzureCli =>
                new ServiceBusClient(
                    worksOptions.AzureCli?.FullyQualifiedNamespace
                    ?? throw new InvalidOperationException(
                        $"EventBusOptions.AzureCli.FullyQualifiedNamespace is required when AuthenticationType is '{nameof(EventBusAuthenticationType.AzureCli)}'"),
                    new AzureCliCredential()),

            EventBusAuthenticationType.ApplicationRegistration =>
                new ServiceBusClient(
                    worksOptions.ApplicationRegistration?.FullyQualifiedNamespace
                    ?? throw new InvalidOperationException(
                        $"EventBusOptions.ApplicationRegistration.FullyQualifiedNamespace is required when AuthenticationType is '{nameof(EventBusAuthenticationType.ApplicationRegistration)}'"),
                    new ClientSecretCredential(
                        worksOptions.ApplicationRegistration.TenantId,
                        worksOptions.ApplicationRegistration.ClientId,
                        worksOptions.ApplicationRegistration.ClientSecret)),

            _ => throw new InvalidOperationException(
                $"Unsupported EventBus authentication type: '{worksOptions.AuthenticationType}'. " +
                $"Valid values are: {string.Join(", ", Enum.GetNames<EventBusAuthenticationType>())}")
        };
}
