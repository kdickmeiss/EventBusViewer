using System.Reflection;

namespace BusWorks;

/// <summary>
/// Holds assemblies to scan for ServiceBus consumers.
/// </summary>
public sealed class ServiceBusAssemblyRegistry(params Assembly[] assemblies)
{
    internal IEnumerable<Assembly> GetAssemblies() => assemblies;
}
