namespace BusWorks.Viewer.Models;

public sealed class AppSettings
{
    public const string SectionName = "AppPreferences";

    public bool IsDarkMode { get; init; } = true;
}
