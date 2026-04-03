using System.Reflection;
using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Consumer;

namespace BusWorks.BackgroundServices;

internal static class ServiceBusEndpointResolver
{
    /// <summary>
    /// Resolves the <see cref="ServiceBusEndpoint"/> for the given consumer type by inspecting
    /// its <see cref="ServiceBusQueueAttribute"/> or <see cref="ServiceBusTopicAttribute"/>.
    /// The queue/topic name is either taken from the attribute directly or derived from the
    /// <see cref="QueueRouteAttribute"/> / <see cref="TopicRouteAttribute"/> on the message type.
    /// </summary>
    /// <param name="consumerType">The consumer class to inspect.</param>
    /// <returns>A fully populated <see cref="ServiceBusEndpoint"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the consumer has no routing attribute, an invalid <c>MaxDeliveryCount</c>,
    /// or the route cannot be resolved from the message type.
    /// </exception>
    public static ServiceBusEndpoint Resolve(Type consumerType)
    {
        Type? messageType = GetConsumerMessageType(consumerType);

        ServiceBusQueueAttribute? queueAttr = consumerType.GetCustomAttribute<ServiceBusQueueAttribute>();
        if (queueAttr is not null)
        {
            if (queueAttr.MaxDeliveryCount < 0)
                throw new InvalidOperationException(
                    $"Consumer '{consumerType.Name}' has an invalid MaxDeliveryCount of {queueAttr.MaxDeliveryCount}. " +
                    $"Value must be >= 0.");

            string queueName = queueAttr.QueueName ?? ResolveQueueNameFromMessageType(consumerType, messageType);
            return new ServiceBusEndpoint(queueName, RequireSession: queueAttr.RequireSession, MaxDeliveryCount: queueAttr.MaxDeliveryCount);
        }

        ServiceBusTopicAttribute? topicAttr = consumerType.GetCustomAttribute<ServiceBusTopicAttribute>();
        if (topicAttr is not null)
        {
            if (topicAttr.MaxDeliveryCount < 0)
                throw new InvalidOperationException(
                    $"Consumer '{consumerType.Name}' has an invalid MaxDeliveryCount of {topicAttr.MaxDeliveryCount}. " +
                    $"Value must be >= 0.");

            string topicName = ResolveTopicNameFromMessageType(consumerType, messageType);
            return new ServiceBusEndpoint(topicName, topicAttr.SubscriptionName, topicAttr.RequireSession, topicAttr.MaxDeliveryCount);
        }

        throw new InvalidOperationException(
            $"Consumer '{consumerType.Name}' must have either:\n" +
            $"  - [ServiceBusQueue] or [ServiceBusQueue(\"queue-name\")] for queues\n" +
            $"  - [ServiceBusTopic(\"subscription-name\")] for topic subscriptions\n\n" +
            $"The queue/topic name is resolved from [QueueRoute] / [TopicRoute] on the message type,\n" +
            $"or can be overridden by passing it directly to [ServiceBusQueue(\"explicit-name\")].");
    }

    /// <summary>
    /// Returns the generic message type <c>TMessage</c> that the consumer handles by inspecting
    /// its <see cref="IConsumer{TMessage}"/> interface implementation.
    /// </summary>
    /// <param name="consumerType">The consumer class to inspect.</param>
    /// <returns>
    /// The <c>TMessage</c> type argument, or <c>null</c> if the consumer does not implement
    /// <see cref="IConsumer{TMessage}"/>.
    /// </returns>
    public static Type? GetConsumerMessageType(Type consumerType)
    {
        return consumerType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>))
            .Select(i => i.GetGenericArguments()[0])
            .FirstOrDefault();
    }

    /// <summary>
    /// Returns a human-readable description of the endpoint for use in log messages and telemetry.
    /// </summary>
    /// <param name="endpoint">The endpoint to describe.</param>
    /// <returns>
    /// <c>"Queue: {name}"</c> for queues, or <c>"Topic: {name}, Subscription: {sub}"</c> for topics.
    /// </returns>
    public static string GetEndpointDescription(ServiceBusEndpoint endpoint) =>
        endpoint.IsQueue
            ? $"Queue: {endpoint.QueueOrTopicName}"
            : $"Topic: {endpoint.QueueOrTopicName}, Subscription: {endpoint.SubscriptionName}";

    /// <summary>
    /// Resolves the queue name from the <see cref="QueueRouteAttribute"/> on the message type.
    /// Throws a descriptive <see cref="InvalidOperationException"/> if the message type is missing,
    /// has no <see cref="QueueRouteAttribute"/>, or is incorrectly decorated with <see cref="TopicRouteAttribute"/>.
    /// </summary>
    private static string ResolveQueueNameFromMessageType(Type consumerType, Type? messageType)
    {
        if (messageType is null)
            throw new InvalidOperationException(
                $"Consumer '{consumerType.Name}' has [ServiceBusQueue] without an explicit queue name, " +
                $"but does not implement IConsumer<TMessage> so the name cannot be resolved automatically. " +
                $"Pass the name explicitly: [ServiceBusQueue(\"queue-name\")].");

        QueueRouteAttribute? queueRoute = messageType.GetCustomAttribute<QueueRouteAttribute>();
        if (queueRoute is not null)
            return queueRoute.QueueName;

        TopicRouteAttribute? topicRoute = messageType.GetCustomAttribute<TopicRouteAttribute>();
        if (topicRoute is not null)
            throw new InvalidOperationException(
                $"Consumer '{consumerType.Name}' has [ServiceBusQueue] but '{messageType.Name}' " +
                $"is declared as a topic via [TopicRoute(\"{topicRoute.TopicName}\")], not a queue. " +
                $"Did you mean [ServiceBusTopic(\"your-subscription-name\")] on '{consumerType.Name}'?");

        throw new InvalidOperationException(
            $"Consumer '{consumerType.Name}' has [ServiceBusQueue] without an explicit queue name, " +
            $"but message type '{messageType.Name}' does not have a [QueueRoute] attribute. " +
            $"Either add [QueueRoute(\"queue-name\")] to '{messageType.Name}', " +
            $"or pass the name explicitly: [ServiceBusQueue(\"queue-name\")].");
    }

    /// <summary>
    /// Resolves the topic name from the <see cref="TopicRouteAttribute"/> on the message type.
    /// Throws a descriptive <see cref="InvalidOperationException"/> if the message type is missing,
    /// has no <see cref="TopicRouteAttribute"/>, or is incorrectly decorated with <see cref="QueueRouteAttribute"/>.
    /// </summary>
    private static string ResolveTopicNameFromMessageType(Type consumerType, Type? messageType)
    {
        if (messageType is null)
            throw new InvalidOperationException(
                $"Consumer '{consumerType.Name}' has [ServiceBusTopic], but does not implement IConsumer<TMessage>. " +
                $"The topic name cannot be resolved automatically. " +
                $"Use IConsumer<TMessage> with [TopicRoute(\"topic-name\")] on the message type.");

        TopicRouteAttribute? topicRoute = messageType.GetCustomAttribute<TopicRouteAttribute>();
        if (topicRoute is not null)
            return topicRoute.TopicName;

        QueueRouteAttribute? queueRoute = messageType.GetCustomAttribute<QueueRouteAttribute>();
        if (queueRoute is not null)
            throw new InvalidOperationException(
                $"Consumer '{consumerType.Name}' has [ServiceBusTopic] but '{messageType.Name}' " +
                $"is declared as a queue via [QueueRoute(\"{queueRoute.QueueName}\")], not a topic. " +
                $"Did you mean [ServiceBusQueue] on '{consumerType.Name}'?");

        throw new InvalidOperationException(
            $"Consumer '{consumerType.Name}' has [ServiceBusTopic], but message type '{messageType.Name}' " +
            $"does not have a [TopicRoute] attribute. " +
            $"Add [TopicRoute(\"topic-name\")] to '{messageType.Name}'.");
    }
}

