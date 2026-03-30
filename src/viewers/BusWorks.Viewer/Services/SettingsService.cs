using System.Text.Json;
using BusWorks.Viewer.Models;

namespace BusWorks.Viewer.Services;

/// <summary>
/// Singleton service that manages application and Service Bus settings.
/// Reads from IConfiguration (appsettings.json + user-settings.json) on startup.
/// Persists user changes to user-settings.json in the content root.
/// </summary>
public sealed class SettingsService
{
    private readonly string _userSettingsPath;
    private ServiceBusSettings _serviceBus;
    private AppSettings _app;

    /// <summary>Raised on the calling thread whenever settings are saved.</summary>
    public event Action? OnChange;

    public ServiceBusSettings ServiceBus => _serviceBus;
    public AppSettings App => _app;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_serviceBus.AdministrationConnectionString) &&
        !string.IsNullOrWhiteSpace(_serviceBus.ClientConnectionString);

    public SettingsService(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _userSettingsPath = Path.Combine(environment.ContentRootPath, "user-settings.json");

        _serviceBus = configuration.GetSection(ServiceBusSettings.SectionName)
                          .Get<ServiceBusSettings>() ?? new ServiceBusSettings();

        _app = configuration.GetSection(AppSettings.SectionName)
                   .Get<AppSettings>() ?? new AppSettings();
    }

    public async Task SaveConnectionSettingsAsync(ServiceBusSettings settings)
    {
        _serviceBus = settings;
        await PersistAsync();
        OnChange?.Invoke();
    }

    public async Task SaveAppSettingsAsync(AppSettings settings)
    {
        _app = settings;
        await PersistAsync();
        OnChange?.Invoke();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private async Task PersistAsync()
    {
        var root = new Dictionary<string, object>
        {
            [ServiceBusSettings.SectionName] = _serviceBus,
            [AppSettings.SectionName] = _app
        };

        string json = JsonSerializer.Serialize(root, JsonOptions);
        await File.WriteAllTextAsync(_userSettingsPath, json);
    }
}

