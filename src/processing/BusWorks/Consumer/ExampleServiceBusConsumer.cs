using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BusWorks.Abstractions;
using BusWorks.Attributes;
using Microsoft.Extensions.Logging;

namespace BusWorks.Consumer;

/// <summary>
/// Example integration events used by the examples below.
/// These inherit from the real IntegrationEvent so they satisfy the
/// ServiceBusConsumer&lt;TMessage&gt; constraint (where TMessage : IIntegrationEvent).
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
public class ExampleServiceBusConsumer : ServiceBusConsumer<UserCreatedEvent>
{
    protected override async Task ProcessMessageAsync(
        UserCreatedEvent message,  // Already deserialized! ✨
        ServiceBusReceivedMessage originalMessage, 
        CancellationToken cancellationToken)
    {
        // OpenTelemetry automatically traces:
        // ✅ Message ID, correlation ID, delivery count
        // ✅ Processing duration, success/failure status
        // ✅ Queue name, message size, enqueued time
        // ✅ Exception details (if any)
        //
        // Just write your business logic!
        
        await SendWelcomeEmail(message.Email);
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
public class OrderCreatedConsumer : ServiceBusConsumer<OrderPlacedEvent>
{
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(ILogger<OrderCreatedConsumer> logger)
    {
        _logger = logger;
    }

    protected override async Task ProcessMessageAsync(
        OrderPlacedEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        // OpenTelemetry handles infrastructure logging automatically.
        // Only log BUSINESS-CRITICAL events:
        
        await ProcessOrder(message);
        
        // ✅ Good: Log business outcome
        _logger.LogInformation(
            "Order {OrderNumber} processed successfully. Total: ${Total}",
            message.OrderNumber,
            message.Total);
        
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
public class UserEventsEmailConsumer : ServiceBusConsumer<UserCreatedEvent>
{
    protected override async Task ProcessMessageAsync(
        UserCreatedEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        // OpenTelemetry automatically captures:
        // - Topic name, subscription name
        // - All message metadata
        await SendWelcomeEmail(message.Email);
    }
    
    private Task SendWelcomeEmail(string email) => Task.CompletedTask;
}

/// <summary>
/// Another subscription to the SAME topic - demonstrates pub/sub.
/// Both UserEventsEmailConsumer and UserEventsNotificationConsumer receive the same message!
/// </summary>
[ServiceBusTopic("notification-service-subscription")]
public class UserEventsNotificationConsumer : ServiceBusConsumer<UserCreatedEvent>
{
    protected override async Task ProcessMessageAsync(
        UserCreatedEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        await CreateNotification(message);
    }
    
    private Task CreateNotification(UserCreatedEvent message) => Task.CompletedTask;
}

// ========================================
// EXAMPLE 4: Topic with Constants
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
public class NotificationAlertsConsumer : ServiceBusConsumer<NotificationEvent>
{
    protected override async Task ProcessMessageAsync(
        NotificationEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        // No infrastructure logging needed - OpenTelemetry handles it
        await ProcessAlert(message);
    }
    
    private Task ProcessAlert(NotificationEvent message) => Task.CompletedTask;
}

// ========================================
// EXAMPLE 5: Non-Generic Consumer (Raw Message Processing)
// ========================================

/// <summary>
/// Non-generic consumer for raw message processing.
/// Use when you need full control or processing non-JSON messages.
/// OpenTelemetry still traces everything automatically.
/// </summary>
[ServiceBusQueue("raw-messages-queue")]
public class RawMessageConsumer : ServiceBusConsumer
{
    protected override async Task ProcessMessageAsync(
        ServiceBusReceivedMessage message, 
        CancellationToken cancellationToken)
    {
        // OpenTelemetry automatically traces MessageId, DeliveryCount, etc.
        // No need to log them!
        
        // Process raw message body
        string messageBody = message.Body.ToString();
        await ProcessRawMessage(messageBody);
    }
    
    private Task ProcessRawMessage(string body) => Task.CompletedTask;
}

// ========================================
// EXAMPLE 6: Custom JSON Serialization Options
// ========================================

/// <summary>
/// Override JsonSerializerOptions to customize deserialization.
/// Always use a static readonly field — JsonSerializer builds an internal reflection
/// cache per options instance. new() in a getter rebuilds it on every message.
/// </summary>
[ServiceBusQueue("custom-serialization-queue")]
public class CustomSerializationConsumer : ServiceBusConsumer<IntegrationEvent>
{
    // ✅ static readonly — one instance, cache built once
    private static readonly JsonSerializerOptions CustomOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    protected override JsonSerializerOptions JsonSerializerOptions => CustomOptions;

    protected override async Task ProcessMessageAsync(
        IntegrationEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        // Custom serialization options applied automatically
        await ProcessEvent(message);
    }
    
    private Task ProcessEvent(IntegrationEvent evt) => Task.CompletedTask;
}

// ========================================
// EXAMPLE 7: Accessing Service Bus Metadata (When Needed)
// ========================================

/// <summary>
/// Demonstrates accessing Service Bus message metadata for idempotency checks.
/// NOTE: Most metadata is already captured by OpenTelemetry spans!
/// Only access when you need it for business logic (e.g., idempotency).
///
/// For limiting retry/delivery attempts, use <c>MaxDeliveryCount</c> on the attribute instead
/// of manually checking <c>DeliveryCount</c> here — see Example 10.
/// </summary>
[ServiceBusQueue("metadata-example-queue")]
public class MetadataAccessConsumer : ServiceBusConsumer<IntegrationEvent>
{
    private readonly ILogger<MetadataAccessConsumer> _logger;

    public MetadataAccessConsumer(ILogger<MetadataAccessConsumer> logger)
    {
        _logger = logger;
    }

    protected override async Task ProcessMessageAsync(
        IntegrationEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        // OpenTelemetry automatically captures most metadata in spans:
        // - MessageId, CorrelationId, DeliveryCount
        // - SequenceNumber, EnqueuedTime, Body size
        //
        // Only access metadata when you NEED it for business logic:

        // ✅ Good: Idempotency check using MessageId
        if (await IsAlreadyProcessed(originalMessage.MessageId))
        {
            _logger.LogInformation("Message {MessageId} already processed (idempotent)", originalMessage.MessageId);
            return;
        }

        // ❌ Bad: Don't manually check DeliveryCount to limit retries — use MaxDeliveryCount on the attribute (see Example 10)
        // if (originalMessage.DeliveryCount > 5) { ... }

        await ProcessEvent(message);
        await MarkAsProcessed(originalMessage.MessageId);
    }

    private Task<bool> IsAlreadyProcessed(string messageId) => Task.FromResult(false);
    private Task MarkAsProcessed(string messageId) => Task.CompletedTask;
    private Task ProcessEvent(IntegrationEvent evt) => Task.CompletedTask;
}

// ========================================
// EXAMPLE 8: Session Consumer - FIFO per SessionId (Queue)
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
/// Access the SessionId via originalMessage.SessionId - it's already there!
/// </summary>
[ServiceBusQueue("payment-commands", RequireSession = true)]
public class PaymentCommandConsumer : ServiceBusConsumer<PaymentCommand>
{
    protected override async Task ProcessMessageAsync(
        PaymentCommand message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        // SessionId is the key that groups related messages together (e.g., customerId)
        string customerId = originalMessage.SessionId;

        // Messages for the SAME customerId are guaranteed to arrive here in order.
        // Messages for DIFFERENT customerIds are processed concurrently.
        await ProcessPaymentInOrder(customerId, message);
    }

    private Task ProcessPaymentInOrder(string customerId, PaymentCommand command) => Task.CompletedTask;
}


// ========================================
// EXAMPLE 9: Session Consumer - FIFO per SessionId (Topic)
// ========================================

/// <summary>
/// Session consumer on a topic subscription.
/// Same guarantees as queue sessions but in a pub/sub topology.
/// The subscription MUST have sessions enabled in Azure Service Bus.
/// The message type MUST implement <see cref="ISessionedEvent"/>.
/// </summary>
[ServiceBusTopic("fulfillment-subscription", RequireSession = true)]
public class OrderFulfillmentConsumer : ServiceBusConsumer<OrderPlacedEvent>
{
    protected override async Task ProcessMessageAsync(
        OrderPlacedEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        // All events for the same orderId arrive here strictly in order
        string orderId = originalMessage.SessionId;

        await FulfillOrder(orderId, message);
    }

    private Task FulfillOrder(string orderId, OrderPlacedEvent evt) => Task.CompletedTask;
}

// ========================================
// EXAMPLE 10: Limiting Delivery Attempts (MaxDeliveryCount)
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
public class PaymentEventConsumer : ServiceBusConsumer<OrderPlacedEvent>
{
    protected override async Task ProcessMessageAsync(
        OrderPlacedEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        // If this throws, the framework will:
        // - Attempt 1 → abandon (DeliveryCount 1 < 3)
        // - Attempt 2 → abandon (DeliveryCount 2 < 3)
        // - Attempt 3 → dead-letter (DeliveryCount 3 >= 3) ✅
        await ProcessPayment(message);
    }

    private Task ProcessPayment(OrderPlacedEvent evt) => Task.CompletedTask;
}

/// <summary>
/// Works exactly the same on topic subscriptions.
/// </summary>
[ServiceBusTopic( "payments-subscription", MaxDeliveryCount = 5)]
public class OrderPaymentTopicConsumer : ServiceBusConsumer<OrderPlacedEvent>
{
    protected override async Task ProcessMessageAsync(
        OrderPlacedEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        await ProcessPayment(message);
    }

    private Task ProcessPayment(OrderPlacedEvent evt) => Task.CompletedTask;
}

/// <summary>
/// Can be combined with <c>RequireSession</c> — both behaviours apply independently.
/// </summary>
[ServiceBusQueue("ordered-payments", RequireSession = true, MaxDeliveryCount = 3)]
public class OrderedPaymentConsumer : ServiceBusConsumer<PaymentCommand>
{
    protected override async Task ProcessMessageAsync(
        PaymentCommand message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        string customerId = originalMessage.SessionId;
        await ProcessPaymentInOrder(customerId, message);
    }

    private Task ProcessPaymentInOrder(string customerId, PaymentCommand command) => Task.CompletedTask;
}

