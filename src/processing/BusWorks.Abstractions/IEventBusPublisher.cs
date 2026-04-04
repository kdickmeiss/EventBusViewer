using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Events;

namespace BusWorks.Abstractions;

public interface IEventBusPublisher
{
    /// <summary>
    /// Publishes <paramref name="event"/> to the Service Bus destination declared by
    /// <see cref="QueueRouteAttribute"/> or <see cref="TopicRouteAttribute"/>
    /// on <typeparamref name="TEvent"/>.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
}
