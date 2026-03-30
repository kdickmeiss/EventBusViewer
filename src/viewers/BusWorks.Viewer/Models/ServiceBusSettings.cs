namespace BusWorks.Viewer.Models;

public sealed class ServiceBusSettings
{
    public const string SectionName = "ServiceBus";

    public string AdministrationConnectionString { get; set; } = string.Empty;
    public string ClientConnectionString { get; set; } = string.Empty;
}

