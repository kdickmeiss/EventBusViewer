using System.Reflection;
using BusWorks.Abstractions.Consumer;
using BusWorks.Consumer;

namespace BusWorks;

/// <summary>
/// Holds the assemblies registered via <see cref="DependencyInjection.AddBussWorks"/> and
/// exposes the consumer types discovered within them.
/// Consumers can live in any number of class libraries — pass each assembly once at registration.
/// </summary>
public sealed class ServiceBusAssemblyRegistry(params Assembly[] assemblies)
{
    private readonly IReadOnlyList<Type> _consumerTypes = assemblies
        .Distinct()
        .SelectMany(SafeGetTypes)
        .Where(t => t is { IsClass: true, IsAbstract: false } &&
                    t.GetInterfaces().Any(i =>
                        i.IsGenericType &&
                        i.GetGenericTypeDefinition() == typeof(IConsumer<>)))
        .ToList();

    internal IReadOnlyList<Type> GetConsumerTypes() => _consumerTypes;

    /// <summary>
    /// Safely retrieves types from an assembly, handling <see cref="ReflectionTypeLoadException"/>.
    /// This can occur when an assembly references a dependency that is not present at runtime.
    /// In that case the successfully loaded types are returned rather than failing entirely.
    /// </summary>
    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
