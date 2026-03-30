namespace BusWorks.Options;

public enum EventBusAuthenticationType
{
    /// <summary>Use a full Azure Service Bus connection string (Development / local emulator).</summary>
    ConnectionString,

    /// <summary>Use a system- or user-assigned Azure Managed Identity.</summary>
    ManagedIdentity,

    /// <summary>Use an Azure AD App Registration (client-credentials flow).</summary>
    ApplicationRegistration,
    
    /// <summary>Use the Azure CLI's logged-in user for authentication. This is intended for local development and testing only.</summary>
    AzureCli
}

public sealed class EventBusOptions
{
    public const string SectionName = "EventBusOptions";

    /// <summary>Selects the Azure Service Bus authentication strategy.</summary>
    public EventBusAuthenticationType AuthenticationType { get; init; }

    public ConnectionStringOptions? ConnectionString { get; init; }
    public ManagedIdentityOptions? ManagedIdentity { get; init; }
    public ApplicationRegistrationOptions? ApplicationRegistration { get; init; }
    
    public AzureCliOptions? AzureCli { get; init; }

    /// <summary>Max concurrent message-processing calls for non-session processors. Default: 10.</summary>
    public int MaxConcurrentCalls { get; init; } = 10;

    /// <summary>Max concurrent sessions for session-aware processors. Default: 8.</summary>
    public int MaxConcurrentSessions { get; init; } = 8;

    /// <summary>Max concurrent calls per session for session-aware processors. Default: 1.</summary>
    public int MaxConcurrentCallsPerSession { get; init; } = 1;
}

public sealed class ConnectionStringOptions
{
    /// <summary>Full Azure Service Bus connection string (includes endpoint and SAS key).</summary>
    public string ConnectionString { get; init; } = string.Empty;
}

public sealed class ManagedIdentityOptions
{
    /// <summary>
    /// The fully qualified Service Bus namespace, e.g. <c>my-namespace.servicebus.windows.net</c>.
    /// </summary>
    public string FullyQualifiedNamespace { get; init; } = string.Empty;

    /// <summary>
    /// Client ID of a user-assigned managed identity.
    /// Leave <c>null</c> to use the system-assigned identity.
    /// </summary>
    public string? ClientId { get; init; }
}

public sealed class ApplicationRegistrationOptions
{
    /// <summary>
    /// The fully qualified Service Bus namespace, e.g. <c>my-namespace.servicebus.windows.net</c>.
    /// </summary>
    public string FullyQualifiedNamespace { get; init; } = string.Empty;

    /// <summary>Azure AD tenant ID.</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Azure AD application (client) ID.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Azure AD client secret.</summary>
    public string ClientSecret { get; init; } = string.Empty;
}

public sealed class AzureCliOptions
{
    /// <summary>
    /// The fully qualified Service Bus namespace, e.g. <c>my-namespace.servicebus.windows.net</c>.
    /// </summary>
    public string FullyQualifiedNamespace { get; init; } = string.Empty;
}

