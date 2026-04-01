using System.Reflection;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions;
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
        EventBusOptions options = configuration
                                      .GetSection(EventBusOptions.SectionName)
                                      .Get<EventBusOptions>()
                                  ?? throw new InvalidOperationException(
                                      $"The '{EventBusOptions.SectionName}' configuration section is missing or empty.");
        
        services.Configure<EventBusOptions>(configuration.GetSection(EventBusOptions.SectionName));

        return services.AddBusWorksCore(options, consumerAssemblies);
    }
    
    /// <summary>
    /// Registers BusWorks services using a pre-built <see cref="EventBusOptions"/> instance.
    /// </summary>
    /// <remarks>
    /// Use this overload when options are constructed programmatically or in test scenarios.
    /// The provided <paramref name="options"/> instance is registered as a singleton
    /// and made available via <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/>.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="options">The fully configured <see cref="EventBusOptions"/> instance.</param>
    /// <param name="consumerAssemblies">
    /// One or more assemblies to scan for <see cref="IIntegrationEvent"/> consumer implementations.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddBusWorks(
        this IServiceCollection services,
        EventBusOptions options,
        params Assembly[] consumerAssemblies)
    {
        
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));

        return services.AddBusWorksCore(options, consumerAssemblies);
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
    /// <param name="configure">A delegate that configures the <see cref="EventBusOptions"/>.</param>
    /// <param name="consumerAssemblies">
    /// One or more assemblies to scan for <see cref="IIntegrationEvent"/> consumer implementations.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddBusWorks(
        this IServiceCollection services,
        Action<EventBusOptions> configure,
        params Assembly[] consumerAssemblies)
    {
        var options = new EventBusOptions();
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
    /// <param name="options">The resolved <see cref="EventBusOptions"/>.</param>
    /// <param name="consumerAssemblies">Assemblies to scan for consumer implementations.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    private static IServiceCollection AddBusWorksCore(
        this IServiceCollection services,
        EventBusOptions options,
        params Assembly[] consumerAssemblies)
    {

        ServiceBusClient serviceBusClient = GetServiceBusClientByConfig(options);

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
    /// <see cref="EventBusOptions.AuthenticationType"/> specified in <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The <see cref="EventBusOptions"/> containing authentication configuration.</param>
    /// <returns>A fully configured <see cref="ServiceBusClient"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a required configuration value for the selected authentication type is missing,
    /// or when an unsupported <see cref="EventBusAuthenticationType"/> value is specified.
    /// </exception>
    private static ServiceBusClient GetServiceBusClientByConfig(EventBusOptions options) =>
        options.AuthenticationType switch
            {
                EventBusAuthenticationType.ConnectionString =>
                    new ServiceBusClient(
                        options.ConnectionString?.ConnectionString
                        ?? throw new InvalidOperationException(
                            $"EventBusOptions.ConnectionString.ConnectionString is required when AuthenticationType is '{nameof(EventBusAuthenticationType.ConnectionString)}'")),

                EventBusAuthenticationType.ManagedIdentity =>
                    new ServiceBusClient(
                        options.ManagedIdentity?.FullyQualifiedNamespace
                        ?? throw new InvalidOperationException(
                            $"EventBusOptions.ManagedIdentity.FullyQualifiedNamespace is required when AuthenticationType is '{nameof(EventBusAuthenticationType.ManagedIdentity)}'"),
                        options.ManagedIdentity.ClientId is { Length: > 0 } clientId
                            ? new ManagedIdentityCredential(clientId)   // user-assigned
                            : new ManagedIdentityCredential()),          // system-assigned
                
                EventBusAuthenticationType.AzureCli =>
                    new ServiceBusClient(
                        options.AzureCli?.FullyQualifiedNamespace
                        ?? throw new InvalidOperationException(
                            $"EventBusOptions.AzureCli.FullyQualifiedNamespace is required when AuthenticationType is '{nameof(EventBusAuthenticationType.AzureCli)}'"),
                        new AzureCliCredential()),

                EventBusAuthenticationType.ApplicationRegistration =>
                    new ServiceBusClient(
                        options.ApplicationRegistration?.FullyQualifiedNamespace
                        ?? throw new InvalidOperationException(
                            $"EventBusOptions.ApplicationRegistration.FullyQualifiedNamespace is required when AuthenticationType is '{nameof(EventBusAuthenticationType.ApplicationRegistration)}'"),
                        new ClientSecretCredential(
                            options.ApplicationRegistration.TenantId,
                            options.ApplicationRegistration.ClientId,
                            options.ApplicationRegistration.ClientSecret)),

                _ => throw new InvalidOperationException(
                    $"Unsupported EventBus authentication type: '{options.AuthenticationType}'. " +
                    $"Valid values are: {string.Join(", ", Enum.GetNames<EventBusAuthenticationType>())}")
            };
}
