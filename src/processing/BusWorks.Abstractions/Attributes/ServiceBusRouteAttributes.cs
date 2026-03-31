namespace BusWorks.Attributes;

/// <summary>
/// Apply to an integration event record to declare that it is routed to a Service Bus <b>queue</b>.
/// Both the publisher (<see cref="IEventBusPublisher.PublishAsync{TEvent}"/>) and the consumer
/// (<c>[ServiceBusQueue]</c> without an explicit name) resolve the queue name from this attribute,
/// so the name only needs to be defined in one place.
/// </summary>
/// <example>
/// <code>
/// [QueueRoute("resort-created")]
/// public record ResortCreatedIntegrationEvent(...) : IntegrationEvent(...);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class QueueRouteAttribute(string queueName) : Attribute
{
    /// <summary>The Service Bus queue name.</summary>
    public string QueueName { get; } = queueName;
}

/// <summary>
/// Apply to an integration event record to declare that it is routed to a Service Bus <b>topic</b>.
/// The publisher resolves the topic name from this attribute automatically.
/// Consumers still must supply their own subscription name via <c>[ServiceBusTopic("subscription-name")]</c>
/// because a single topic can have many independent subscriptions.
/// </summary>
/// <example>
/// <code>
/// [TopicRoute("park-events")]
/// public record AttractionUpdatedIntegrationEvent(...) : IntegrationEvent(...);
///
/// // Consumer – subscription name is consumer-specific, topic name comes from the attribute above.
/// [ServiceBusTopic("theme-park-service")]
/// public class AttractionUpdatedConsumer : IConsumer&lt;AttractionUpdatedIntegrationEvent&gt; { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TopicRouteAttribute(string topicName) : Attribute
{
    /// <summary>The Service Bus topic name.</summary>
    public string TopicName { get; } = topicName;
}

