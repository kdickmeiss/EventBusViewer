using System.Text.Json;
using BusWorks.Abstractions;
using BusWorks.Abstractions.Consumer;

namespace BusWorks.Consumer;

/// <summary>
/// Shared JSON deserialization defaults, declared as a single static instance so
/// JsonSerializer's internal reflection cache is built once and reused across all consumers.
/// </summary>
internal static class ServiceBusConsumerDefaults
{
    internal static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

internal sealed class ConsumeContext<TMessage>(
    TMessage message,
    MessageContext metadata,
    CancellationToken cancellationToken) : IConsumeContext<TMessage>
    where TMessage : class, IIntegrationEvent
{
    public TMessage Message { get; } = message;
    public MessageContext Metadata { get; } = metadata;
    public CancellationToken CancellationToken { get; } = cancellationToken;
}
