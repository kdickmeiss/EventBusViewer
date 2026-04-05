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

    /// <summary>
    /// How long a session-aware processor waits for a new message on an idle session before
    /// releasing that session slot and accepting the next queued session.
    /// Only applies to consumers decorated with <c>[ServiceBusQueue(RequireSession = true)]</c>
    /// or <c>[ServiceBusTopic(RequireSession = true)]</c>; ignored entirely for non-session processors.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why this matters:</strong> The processor holds up to <see cref="MaxConcurrentSessions"/>
    /// session slots simultaneously. After the last message in a session is consumed the processor
    /// keeps the slot open, waiting for more messages from that same session. Without a timeout the
    /// slot stays locked for the full <c>MaxAutoLockRenewalDuration</c> (5 minutes by default).
    /// Once all slots are occupied by idle sessions, new sessions cannot be picked up until a slot
    /// is released — causing processing to stall.
    /// </para>
    /// <para>
    /// <strong>Recommended values by environment:</strong>
    /// <list type="table">
    ///   <listheader><term>Environment</term><description>Value</description></listheader>
    ///   <item><term>Tests</term><description><c>500 ms – 1 s</c> — release slots immediately after each message; no session affinity needed.</description></item>
    ///   <item><term>Development</term><description><c>5 s – 15 s</c> — fast feedback; sessions clear quickly between runs.</description></item>
    ///   <item><term>Production</term><description><c>30 s – 60 s</c> — preserve session affinity for bursty senders while still freeing idle slots within a reasonable time.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// When <c>null</c> (the default) the Azure SDK applies its own internal default, which
    /// effectively behaves like <c>MaxAutoLockRenewalDuration</c>. Always set an explicit value
    /// in environments where sessions are short-lived or tests run in rapid succession.
    /// </para>
    /// </remarks>
    public TimeSpan? SessionIdleTimeout { get; set; }
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

