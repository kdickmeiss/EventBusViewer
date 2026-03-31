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
    public static IServiceCollection AddEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] consumerAssemblies)
    {
        EventBusOptions options = configuration
            .GetSection(EventBusOptions.SectionName)
            .Get<EventBusOptions>()
            ?? throw new InvalidOperationException(
                $"The '{EventBusOptions.SectionName}' configuration section is missing or empty.");

        ServiceBusClient serviceBusClient = options.AuthenticationType switch
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

        services.Configure<EventBusOptions>(configuration.GetSection(EventBusOptions.SectionName));

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
}
