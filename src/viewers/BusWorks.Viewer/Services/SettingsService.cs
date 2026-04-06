using System.Text.Json;
using BusWorks.Viewer.Models;

namespace BusWorks.Viewer.Services;

/// <summary>
/// Singleton service that manages application and Service Bus settings.
/// Reads from IConfiguration (appsettings.json + user-settings.json) on startup.
/// Persists user changes to user-settings.json in the content root.
/// </summary>
public sealed class SettingsService(IConfiguration configuration, IWebHostEnvironment environment)
{
    private readonly string _userSettingsPath = Path.Combine(environment.ContentRootPath, "user-settings.json");

    /// <summary>Raised on the calling thread whenever settings are saved.</summary>
    public event Action? OnChange;

    public ServiceBusSettings ServiceBus { get; private set; } = configuration.GetSection(ServiceBusSettings.SectionName)
        .Get<ServiceBusSettings>() ?? new ServiceBusSettings();

    public AppSettings App { get; private set; } = configuration.GetSection(AppSettings.SectionName)
        .Get<AppSettings>() ?? new AppSettings();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServiceBus.AdministrationConnectionString) &&
        !string.IsNullOrWhiteSpace(ServiceBus.ClientConnectionString);

    public async Task SaveConnectionSettingsAsync(ServiceBusSettings settings)
    {
        ServiceBus = settings;
        await PersistAsync();
        OnChange?.Invoke();
    }

    public async Task SaveAppSettingsAsync(AppSettings settings)
    {
        App = settings;
        await PersistAsync();
        OnChange?.Invoke();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private async Task PersistAsync()
    {
        var root = new Dictionary<string, object>
        {
            [ServiceBusSettings.SectionName] = ServiceBus,
            [AppSettings.SectionName] = App
        };

        string json = JsonSerializer.Serialize(root, JsonOptions);
        await File.WriteAllTextAsync(_userSettingsPath, json);
    }
}
