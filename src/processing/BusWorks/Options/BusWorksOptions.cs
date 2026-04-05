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

public sealed class BusWorksOptions
{
    public const string SectionName = "BusWorksOptions";

    /// <summary>Selects the Azure Service Bus authentication strategy.</summary>
    public EventBusAuthenticationType AuthenticationType { get; set; }

    public ConnectionStringOptions? ConnectionString { get; set; }
    public ManagedIdentityOptions? ManagedIdentity { get; set; }
    public ApplicationRegistrationOptions? ApplicationRegistration { get; set; }
    
    public AzureCliOptions? AzureCli { get; set; }

    /// <summary>Max concurrent message-processing calls for non-session processors. Default: 10.</summary>
    public int MaxConcurrentCalls { get; set; } = 10;

    /// <summary>Max concurrent sessions for session-aware processors. Default: 8.</summary>
    public int MaxConcurrentSessions { get; set; } = 8;

    /// <summary>Max concurrent calls per session for session-aware processors. Default: 1.</summary>
    public int MaxConcurrentCallsPerSession { get; set; } = 1;
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

