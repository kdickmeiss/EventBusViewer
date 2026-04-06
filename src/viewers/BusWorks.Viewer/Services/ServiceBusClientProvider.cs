using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace BusWorks.Viewer.Services;

/// <summary>
/// Singleton that lazily creates and caches <see cref="ServiceBusAdministrationClient"/> and
/// <see cref="ServiceBusClient"/>, recreating them whenever the connection strings change via
/// <see cref="SettingsService"/>. Eliminates duplicated client-management code across services.
/// </summary>
public sealed class ServiceBusClientProvider : IDisposable
{
    private readonly SettingsService _settings;

    private ServiceBusAdministrationClient? _adminClient;
    private ServiceBusClient?               _busClient;
    private string?                         _lastAdminCs;
    private string?                         _lastClientCs;

    public ServiceBusClientProvider(SettingsService settings)
    {
        _settings = settings;
        _settings.OnChange += Invalidate;
    }

    /// <summary>Lazily created admin client — recreated when the connection string changes.</summary>
    public ServiceBusAdministrationClient AdminClient
    {
        get
        {
            string cs = _settings.ServiceBus.AdministrationConnectionString;
            if (_adminClient is not null && _lastAdminCs == cs) return _adminClient;
            _lastAdminCs = cs;
            return _adminClient = new ServiceBusAdministrationClient(cs);
        }
    }

    /// <summary>Lazily created bus client — recreated when the connection string changes.</summary>
    public ServiceBusClient BusClient
    {
        get
        {
            string cs = _settings.ServiceBus.ClientConnectionString;
            if (_busClient is not null && _lastClientCs == cs) return _busClient;
            _lastClientCs = cs;
            ServiceBusClient? old = _busClient;
            _busClient = new ServiceBusClient(cs);
            old?.DisposeAsync().AsTask().ContinueWith(_ => { });
            return _busClient;
        }
    }

    /// <summary>Forces both clients to be recreated on next access.</summary>
    public void Invalidate()
    {
        _adminClient  = null;
        _lastAdminCs  = null;
        _lastClientCs = null;
        ServiceBusClient? old = _busClient;
        _busClient = null;
        old?.DisposeAsync().AsTask().ContinueWith(_ => { });
    }

    public void Dispose()
    {
        _settings.OnChange -= Invalidate;
        _busClient?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

