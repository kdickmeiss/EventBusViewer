using System.Reflection;
using BusWorks.Attributes;

namespace BusWorks;

/// <summary>
/// Utility for resolving the Service Bus destination declared on an integration event type via
/// <see cref="QueueRouteAttribute"/> or <see cref="TopicRouteAttribute"/>.
///
/// Primarily useful in tests (e.g. asserting DLQ messages) and provisioning tools,
/// where you want the type-safe name without hard-coding it a second time.
/// </summary>
/// <example>
/// <code>
/// // In a test:
/// string queue = ServiceBusRoute.GetQueueName&lt;TestIntegrationEvent&gt;();
/// var dlqMessages = await EventBus.WaitForDeadLetterMessagesAsync(queue, expectedCount: 1);
/// </code>
/// </example>
public static class ServiceBusRoute
{
    /// <summary>
    /// Returns the queue name declared by <see cref="QueueRouteAttribute"/> on <typeparamref name="TEvent"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="TEvent"/> does not have a <see cref="QueueRouteAttribute"/>.
    /// </exception>
    public static string GetQueueName<TEvent>() where TEvent : IIntegrationEvent
    {
        Type eventType = typeof(TEvent);
        QueueRouteAttribute? attr = eventType.GetCustomAttribute<QueueRouteAttribute>();

        if (attr is null)
            throw new InvalidOperationException(
                $"Integration event '{eventType.Name}' does not have a [{nameof(QueueRouteAttribute)}]. " +
                $"Add [QueueRoute(\"queue-name\")] to '{eventType.Name}'.");

        return attr.QueueName;
    }

    /// <summary>
    /// Returns the topic name declared by <see cref="TopicRouteAttribute"/> on <typeparamref name="TEvent"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="TEvent"/> does not have a <see cref="TopicRouteAttribute"/>.
    /// </exception>
    public static string GetTopicName<TEvent>() where TEvent : IIntegrationEvent
    {
        Type eventType = typeof(TEvent);
        TopicRouteAttribute? attr = eventType.GetCustomAttribute<TopicRouteAttribute>();

        if (attr is null)
            throw new InvalidOperationException(
                $"Integration event '{eventType.Name}' does not have a [{nameof(TopicRouteAttribute)}]. " +
                $"Add [TopicRoute(\"topic-name\")] to '{eventType.Name}'.");

        return attr.TopicName;
    }
}
