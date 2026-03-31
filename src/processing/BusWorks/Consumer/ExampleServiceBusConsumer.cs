using BusWorks.Abstractions;
using BusWorks.Abstractions.Attributes;
using BusWorks.Abstractions.Consumer;
using Microsoft.Extensions.Logging;

namespace BusWorks.Consumer;

/// <summary>
/// Example integration events used by the examples below.
/// These inherit from the real IntegrationEvent so they satisfy the
/// IConsumer&lt;TMessage&gt; constraint (where TMessage : IIntegrationEvent).
/// </summary>
public record UserCreatedEvent(Guid Id, DateTime OccurredOnUtc, string Email, string Name)
    : IntegrationEvent(Id, OccurredOnUtc);

public record OrderPlacedEvent(Guid Id, DateTime OccurredOnUtc, string OrderNumber, decimal Total)
    : IntegrationEvent(Id, OccurredOnUtc), ISessionedEvent
{
    // All events for the same order are delivered in order. Different orders run concurrently.
    public string SessionId => OrderNumber;
}

public record NotificationEvent(Guid Id, DateTime OccurredOnUtc, string Message)
    : IntegrationEvent(Id, OccurredOnUtc);

public record PaymentCommand(Guid Id, DateTime OccurredOnUtc, string CustomerId, decimal Amount)
    : IntegrationEvent(Id, OccurredOnUtc), ISessionedEvent
{
    // All payments for the same customer are ordered. Different customers run concurrently.
    public string SessionId => CustomerId;
}

// ========================================
// EXAMPLE 1: Minimal Consumer (OpenTelemetry Best Practice)
// ========================================

/// <summary>
/// Best practice: No logging needed - OpenTelemetry handles all tracing automatically!
/// The framework captures message ID, correlation ID, delivery count, duration, etc.
/// </summary>
[ServiceBusQueue("user-created-events")]
public class ExampleServiceBusConsumer : IConsumer<UserCreatedEvent>
{
    public async Task Consume(IConsumeContext<UserCreatedEvent> context)
    {
        // context.Message is already deserialized ✨
        // OpenTelemetry automatically traces:
        // ✅ Message ID, correlation ID, delivery count
        // ✅ Processing duration, success/failure status
        // ✅ Queue name, message size, enqueued time
        // ✅ Exception details (if any)
        //
        // Just write your business logic!

        await SendWelcomeEmail(context.Message.Email);
    }

    private Task SendWelcomeEmail(string email) => Task.CompletedTask;
}

// ========================================
// EXAMPLE 2: When to Use Logging (Business Events Only)
// ========================================

/// <summary>
/// Using constants for shared queue names across the codebase.
/// Shows when logging IS appropriate: business-critical events only.
/// </summary>
public static class QueueNames
{
    public const string UserEvents = "user-events";
    public const string OrderEvents = "order-events";
    public const string PaymentEvents = "payment-events";
}

[ServiceBusQueue(QueueNames.OrderEvents)]
public class OrderCreatedConsumer : IConsumer<OrderPlacedEvent>
{
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(ILogger<OrderCreatedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(IConsumeContext<OrderPlacedEvent> context)
    {
        // OpenTelemetry handles infrastructure logging automatically.
        // Only log BUSINESS-CRITICAL events:

        await ProcessOrder(context.Message);

        // ✅ Good: Log business outcome
        _logger.LogInformation(
            "Order {OrderNumber} processed successfully. Total: ${Total}",
            context.Message.OrderNumber,
            context.Message.Total);

        // ❌ Bad: Don't log infrastructure details (MessageId, DeliveryCount, etc.)
        // These are already in OpenTelemetry spans!
    }

    private Task ProcessOrder(OrderPlacedEvent order) => Task.CompletedTask;
}

// ========================================
// EXAMPLE 3: Topic Subscription (Pub/Sub Pattern)
// ========================================

/// <summary>
/// Subscribe to a topic - perfect for pub/sub patterns.
/// Multiple consumers can subscribe to the same topic with different subscriptions.
/// Each subscription receives a copy of every message published to the topic.
/// OpenTelemetry automatically tracks subscription name in spans.
/// </summary>
[ServiceBusTopic("email-service-subscription")]
public class UserEventsEmailConsumer : IConsumer<UserCreatedEvent>
{
    public async Task Consume(IConsumeContext<UserCreatedEvent> context)
    {
        // OpenTelemetry automatically captures:
        // - Topic name, subscription name
        // - All message metadata
        await SendWelcomeEmail(context.Message.Email);
    }

    private Task SendWelcomeEmail(string email) => Task.CompletedTask;
}

/// <summary>
/// Another subscription to the SAME topic - demonstrates pub/sub.
/// Both UserEventsEmailConsumer and UserEventsNotificationConsumer receive the same message!
/// </summary>
[ServiceBusTopic("notification-service-subscription")]
public class UserEventsNotificationConsumer : IConsumer<UserCreatedEvent>
{
    public async Task Consume(IConsumeContext<UserCreatedEvent> context)
    {
        await CreateNotification(context.Message);
    }

    private Task CreateNotification(UserCreatedEvent message) => Task.CompletedTask;
}

// ========================================
// EXAMPLE 4: Topic with Constants
// ========================================

public static class TopicNames
{
    public const string NotificationsTopic = "notifications-topic";

    // Subscriptions for the notifications topic
    public const string AlertsSubscription = "alerts-subscription";
    public const string EmailSubscription = "email-subscription";
}

[ServiceBusTopic(TopicNames.AlertsSubscription)]
public class NotificationAlertsConsumer : IConsumer<NotificationEvent>
{
    public async Task Consume(IConsumeContext<NotificationEvent> context)
    {
        // No infrastructure logging needed - OpenTelemetry handles it
        await ProcessAlert(context.Message);
    }

    private Task ProcessAlert(NotificationEvent message) => Task.CompletedTask;
}

// ========================================
// EXAMPLE 5: Accessing Service Bus Metadata (When Needed)
// ========================================

/// <summary>
/// Demonstrates accessing Service Bus message metadata for idempotency checks.
/// NOTE: Most metadata is already captured by OpenTelemetry spans!
/// Only access when you need it for business logic (e.g., idempotency).
///
/// For limiting retry/delivery attempts, use <c>MaxDeliveryCount</c> on the attribute instead
/// of manually checking <c>DeliveryCount</c> here — see Example 9.
/// </summary>
[ServiceBusQueue("metadata-example-queue")]
public class MetadataAccessConsumer : IConsumer<IntegrationEvent>
{
    private readonly ILogger<MetadataAccessConsumer> _logger;

    public MetadataAccessConsumer(ILogger<MetadataAccessConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(IConsumeContext<IntegrationEvent> context)
    {
        // OpenTelemetry automatically captures most metadata in spans:
        // - MessageId, CorrelationId, DeliveryCount
        // - SequenceNumber, EnqueuedTime, Body size
        //
        // Only access metadata when you NEED it for business logic:

        // ✅ Good: Idempotency check using MessageId
        if (await IsAlreadyProcessed(context.Metadata.MessageId))
        {
            _logger.LogInformation("Message {MessageId} already processed (idempotent)", context.Metadata.MessageId);
            return;
        }

        // ❌ Bad: Don't manually check DeliveryCount to limit retries — use MaxDeliveryCount on the attribute (see Example 9)
        // if (context.Metadata.DeliveryCount > 5) { ... }

        await ProcessEvent(context.Message);
        await MarkAsProcessed(context.Metadata.MessageId);
    }

    private Task<bool> IsAlreadyProcessed(string messageId) => Task.FromResult(false);
    private Task MarkAsProcessed(string messageId) => Task.CompletedTask;
    private Task ProcessEvent(IntegrationEvent evt) => Task.CompletedTask;
}

// ========================================
// EXAMPLE 7: Session Consumer - FIFO per SessionId (Queue)
// ========================================

/// <summary>
/// Session consumer for guaranteed FIFO ordering per SessionId.
/// Use when messages for the same entity (e.g., same customer, same order) must be processed in order.
///
/// REQUIREMENTS:
/// - The queue MUST have sessions enabled in Azure Service Bus
/// - The message type MUST implement <see cref="ISessionedEvent"/> — the publisher sets SessionId automatically
///
/// HOW IT WORKS:
/// - The processor locks one session at a time (per concurrent slot)
/// - All messages within a session are processed serially (FIFO)
/// - Multiple sessions are processed concurrently (controlled by MaxConcurrentSessions)
///
/// Access the SessionId via context.Metadata.SessionId - it's already there!
/// </summary>
[ServiceBusQueue("payment-commands", RequireSession = true)]
public class PaymentCommandConsumer : IConsumer<PaymentCommand>
{
    public async Task Consume(IConsumeContext<PaymentCommand> context)
    {
        string customerId = context.Metadata.SessionId;

        // Messages for the SAME customerId are guaranteed to arrive here in order.
        // Messages for DIFFERENT customerIds are processed concurrently.
        await ProcessPaymentInOrder(customerId, context.Message);
    }

    private Task ProcessPaymentInOrder(string customerId, PaymentCommand command) => Task.CompletedTask;
}

// ========================================
// EXAMPLE 8: Session Consumer - FIFO per SessionId (Topic)
// ========================================

/// <summary>
/// Session consumer on a topic subscription.
/// Same guarantees as queue sessions but in a pub/sub topology.
/// The subscription MUST have sessions enabled in Azure Service Bus.
/// The message type MUST implement <see cref="ISessionedEvent"/>.
/// </summary>
[ServiceBusTopic("fulfillment-subscription", RequireSession = true)]
public class OrderFulfillmentConsumer : IConsumer<OrderPlacedEvent>
{
    public async Task Consume(IConsumeContext<OrderPlacedEvent> context)
    {
        string orderId = context.Metadata.SessionId;

        await FulfillOrder(orderId, context.Message);
    }

    private Task FulfillOrder(string orderId, OrderPlacedEvent evt) => Task.CompletedTask;
}

// ========================================
// EXAMPLE 9: Limiting Delivery Attempts (MaxDeliveryCount)
// ========================================

/// <summary>
/// Use <c>MaxDeliveryCount</c> on the attribute to dead-letter a message after N delivery attempts,
/// before Azure's entity-level <c>MaxDeliveryCount</c> is reached.
///
/// HOW IT WORKS:
/// - On each failed attempt the framework checks <c>DeliveryCount</c> against <c>MaxDeliveryCount</c>
/// - If <c>DeliveryCount &gt;= MaxDeliveryCount</c>: message is dead-lettered immediately
/// - Otherwise: message is abandoned and re-queued for the next attempt
///
/// WHEN TO USE:
/// - You want tighter control than the Azure entity default (usually 10)
/// - Different consumers on the same queue/topic need different retry budgets
/// - You want to dead-letter faster for non-idempotent or expensive operations
///
/// Default is <c>5</c> when not specified.
/// Set to <c>0</c> to disable code-level enforcement and rely entirely on the Azure entity's <c>MaxDeliveryCount</c> setting.
/// </summary>
[ServiceBusQueue("payment-events", MaxDeliveryCount = 3)]
public class PaymentEventConsumer : IConsumer<OrderPlacedEvent>
{
    public async Task Consume(IConsumeContext<OrderPlacedEvent> context)
    {
        // If this throws, the framework will:
        // - Attempt 1 → abandon (DeliveryCount 1 < 3)
        // - Attempt 2 → abandon (DeliveryCount 2 < 3)
        // - Attempt 3 → dead-letter (DeliveryCount 3 >= 3) ✅
        await ProcessPayment(context.Message);
    }

    private Task ProcessPayment(OrderPlacedEvent evt) => Task.CompletedTask;
}

/// <summary>
/// Works exactly the same on topic subscriptions.
/// </summary>
[ServiceBusTopic("payments-subscription", MaxDeliveryCount = 5)]
public class OrderPaymentTopicConsumer : IConsumer<OrderPlacedEvent>
{
    public async Task Consume(IConsumeContext<OrderPlacedEvent> context)
    {
        await ProcessPayment(context.Message);
    }

    private Task ProcessPayment(OrderPlacedEvent evt) => Task.CompletedTask;
}

/// <summary>
/// Can be combined with <c>RequireSession</c> — both behaviours apply independently.
/// </summary>
[ServiceBusQueue("ordered-payments", RequireSession = true, MaxDeliveryCount = 3)]
public class OrderedPaymentConsumer : IConsumer<PaymentCommand>
{
    public async Task Consume(IConsumeContext<PaymentCommand> context)
    {
        string customerId = context.Metadata.SessionId;
        await ProcessPaymentInOrder(customerId, context.Message);
    }

    private Task ProcessPaymentInOrder(string customerId, PaymentCommand command) => Task.CompletedTask;
}

