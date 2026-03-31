# BusWorks Processing Guide

Comprehensive guide for publishing and consuming messages using BusWorks — a processor for Azure Service Bus messages that hosts a background service for message consumption and publishing, and is extensible for other brokers.

## 📚 Table of Contents

1. [Architecture Overview](#-architecture-overview)
2. [Publishing Messages](#-publishing-messages)
3. [Consuming Messages](#-consuming-messages)
4. [Generic vs Non-Generic Consumers](#-generic-vs-non-generic-consumers)
5. [Topic Subscriptions](#-topic-subscriptions)
6. [Sessions & FIFO Ordering](#-sessions--fifo-ordering)
7. [Consumer Attribute Reference](#-consumer-attribute-reference)
8. [Authentication & Connection](#-authentication--connection)
9. [Global ServiceBus Settings](#-global-servicebus-settings)
10. [Dependency Injection](#-dependency-injection)
11. [OpenTelemetry Observability](#-opentelemetry-observability)
12. [Message Processing Patterns](#-message-processing-patterns)
13. [Error Handling Strategies](#-error-handling-strategies)
14. [Best Practices](#-best-practices)
15. [Troubleshooting](#-troubleshooting)

---

## 🏗️ Architecture Overview

BusWorks follows Clean Architecture — the interface lives in the Application layer, the implementation lives in the Infrastructure layer.

```
BusWorks.Abstractions/
    Messaging/
        IIntegrationEvent.cs         ← base interface for all events
        IntegrationEvent.cs          ← base record for all events
        IEventPublisher.cs           ← publisher abstraction (application layer)
        RouteAttributes.cs           ← [QueueRoute] / [TopicRoute] attributes for events
        RouteHelper.cs               ← Route helper (tests / provisioning tools)

BusWorks/
    Publisher/
        ServiceBusPublisher.cs                   ← IEventPublisher implementation
    Consumer/
        ServiceBusProcessorBackgroundService.cs  ← consumer base classes + background service
        ConsumerDiscovery.cs                     ← consumer discovery
```

### Why split across layers?

The Application layer defines **what** (publish a message and where it goes). The Infrastructure layer defines **how** (use Azure Service Bus or other brokers). Controllers and handlers depend only on `IEventPublisher` — they never know the broker implementation exists.

```
Integration Event
  → [QueueRoute("my-queue")]        (Application layer — declares destination)

Controller / Handler
  → IEventPublisher.PublishAsync (Application layer — concept)
       ↑ implemented by
  ServiceBusPublisher                (Infrastructure layer — Azure SDK, reads [QueueRoute])
```

### Route attributes — single source of truth

The queue or topic name for an integration event is declared **once** on the event record itself via `[QueueRoute]` or `[TopicRoute]` (both live in the Application layer). Both the publisher and the consumer infrastructure read from this attribute automatically, so the name never needs to be repeated anywhere else.

```
[QueueRoute("resort-created")]   ← defined once, on the event
       ↓                                ↓
IEventPublisher.PublishAsync  [ServiceBusQueue] consumer
(reads it automatically)         (reads it automatically)
```

---

## 📤 Publishing Messages

### 1. Define Your Integration Event

Inherit from `IntegrationEvent` and annotate with `[QueueRoute]` (or `[TopicRoute]` for topics):

```csharp
using BusWorks.Abstractions;

[QueueRoute("user-created-events")]
public sealed record UserCreatedIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string Email,
    string Name)
    : IntegrationEvent(Id, OccurredOnUtc);
```

The `[QueueRoute]` attribute is the **single source of truth** for the queue name. Neither the publisher call site nor the consumer attribute needs to repeat it.

### 2. Inject and Publish in a Command Handler

```csharp
using BusWorks.Abstractions;

public class RegisterUserCommandHandler(IEventPublisher eventBus) : ICommandHandler<RegisterUserCommand>
{
    public async ValueTask<ErrorOr<Success>> Handle(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        // ... create user ...

        await eventBus.PublishAsync(
            new UserCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, command.Email, command.Name),
            cancellationToken);

        return Result.Success;
    }
}
```

No queue name string anywhere — the publisher resolves it from `[QueueRoute]` on `UserCreatedIntegrationEvent`.

### 3. Publish from a Controller Endpoint

```csharp
private static async Task<IResult> CreateUserAsync(
    [FromBody] CreateUserRequest request,
    [FromServices] ISender sender,
    [FromServices] IEventPublisher eventBus,
    CancellationToken cancellationToken)
{
    ErrorOr<UserResponse> result = await sender.Send(new CreateUserCommand(request.Email), cancellationToken);

    if (!result.IsError)
    {
        await eventBus.PublishAsync(
            new UserCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, request.Email, request.Name),
            cancellationToken);
    }

    return result.Match(Results.Ok, ProblemExtensions.Problem);
}
```

### IEventPublisher Interface

Defined in `BusWorks.Abstractions`:

```csharp
public interface IEventPublisher
{
    /// Publishes the event to the destination declared by [QueueRoute] or [TopicRoute] on TEvent.
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
}
```

### What ServiceBusPublisher does for you

- ✅ Resolves the destination from `[QueueRoute]` / `[TopicRoute]` on the event type
- ✅ JSON serializes the event
- ✅ Sets `MessageId` and `CorrelationId` from `event.Id`
- ✅ Injects `traceparent` into `ApplicationProperties` for distributed tracing
- ✅ Creates a `SpanKind.Producer` OpenTelemetry span with full semantic conventions
- ✅ Records exceptions and sets error span status on failure

You never interact with `ServiceBusPublisher` directly — always inject `IEventPublisher`.

---

## 📥 Consuming Messages

### Quick Start

Decorate the consumer with `[ServiceBusQueue]` — **no queue name needed** because it is resolved automatically from `[QueueRoute]` on the message type:

```csharp
using BusWorks.Abstractions;
using BusWorks.Consumer;

// Queue name comes from [QueueRoute("user-created-events")] on UserCreatedIntegrationEvent
[ServiceBusQueue]
public class UserCreatedConsumer : ServiceBusConsumer<UserCreatedIntegrationEvent>
{
    protected override async Task ProcessMessageAsync(
        UserCreatedIntegrationEvent message,        // Already deserialized! ✨
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        await SendWelcomeEmail(message.Email);
    }

    private Task SendWelcomeEmail(string email) => Task.CompletedTask;
}
```

Consumers are **automatically discovered** at startup — no DI registration needed.

> 💡 **Note:** OpenTelemetry tracing is automatic! No infrastructure logging needed (message IDs, delivery counts, etc.). Only log business-critical events.

### With an Explicit Override

You can still pass a name directly to `[ServiceBusQueue]` when you need to override or the message type has no `[QueueRoute]` attribute:

```csharp
[ServiceBusQueue("user-created-events")]
public class UserCreatedConsumer : ServiceBusConsumer<UserCreatedIntegrationEvent>
{
    // ...
}
```

The explicit name takes precedence over the route attribute. This is useful for non-generic consumers or edge cases where the event is owned by another module.

### Using RouteHelper in Tests and Tools

When you need the queue name outside of a consumer (e.g. DLQ assertions in tests, provisioning scripts), use the `RouteHelper` — it reads from the same `[QueueRoute]` attribute:

```csharp
string queue = RouteHelper.GetQueueName<UserCreatedIntegrationEvent>();

// In an integration test:
IReadOnlyList<ServiceBusReceivedMessage> dlqMessages =
    await EventBus.WaitForDeadLetterMessagesAsync(queue, expectedCount: 1);
```

This keeps everything in sync — rename the queue in one place (`[QueueRoute]`) and it automatically propagates to publishers, consumers, tests, and tools.

---

## 🎯 Generic vs Non-Generic Consumers

### ServiceBusConsumer\<TMessage\> (Generic — RECOMMENDED) ✅

**Use when:**
- Processing JSON messages
- You want automatic deserialization
- You prefer strongly-typed message objects

```csharp
// Queue name resolved automatically from [QueueRoute] on MyEvent
[ServiceBusQueue]
public class MyConsumer : ServiceBusConsumer<MyEvent>
{
    protected override async Task ProcessMessageAsync(
        MyEvent message,                            // Already deserialized! ✨
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Event: {message.Id}");
    }
}
```

### ServiceBusConsumer (Non-Generic)

**Use when:**
- You need full control over deserialization
- Processing binary / non-JSON messages
- Handling multiple message formats in one consumer

> ⚠️ Non-generic consumers have no message type to read `[QueueRoute]` from. You must pass the queue name explicitly: `[ServiceBusQueue("raw-queue")]`.

```csharp
[ServiceBusQueue("raw-queue")]
public class RawMessageConsumer : ServiceBusConsumer
{
    protected override async Task ProcessMessageAsync(
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        string body = message.Body.ToString();
        // Custom deserialization or raw processing...
    }
}
```

---

## 🔀 Topic Subscriptions

**Queue** = Single consumer per message (point-to-point)  
**Topic + Subscriptions** = Multiple consumers per message (pub/sub)

Declare the topic name once on the event via `[TopicRoute]`. Each consumer then only specifies its own subscription name:

```csharp
// Integration event — topic declared once
[TopicRoute("user-events-topic")]
public sealed record UserCreatedIntegrationEvent(...) : IntegrationEvent(...);

// Email Service — subscription name is this consumer's concern
[ServiceBusTopic("email-service-subscription")]
public class UserEventsEmailConsumer : ServiceBusConsumer<UserCreatedIntegrationEvent>
{
    protected override async Task ProcessMessageAsync(
        UserCreatedIntegrationEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        await SendWelcomeEmail(message.Email);
    }
}

// Notification Service — subscribes to the SAME topic, different subscription
[ServiceBusTopic("notification-service-subscription")]
public class UserEventsNotificationConsumer : ServiceBusConsumer<UserCreatedIntegrationEvent>
{
    protected override async Task ProcessMessageAsync(
        UserCreatedIntegrationEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        await CreateNotification(message);
    }
}
```

One message published to the topic is delivered to **all subscriptions**. The topic name never appears more than once — on the event record.

### Subscription Name Constants (Optional)

The topic name lives on the event. You only need constants for subscription names if they are shared between multiple places (e.g. provisioning + consumer):

```csharp
public static class SubscriptionNames
{
    public const string EmailService        = "email-service-subscription";
    public const string NotificationService = "notification-service-subscription";
}

[ServiceBusTopic(SubscriptionNames.EmailService)]
public class UserEventsEmailConsumer : ServiceBusConsumer<UserCreatedIntegrationEvent> { }

[ServiceBusTopic(SubscriptionNames.NotificationService)]
public class UserEventsNotificationConsumer : ServiceBusConsumer<UserCreatedIntegrationEvent> { }
```

---

## 🔒 Sessions & FIFO Ordering

> ⚠️ **Current implementation scope — basic FIFO only.**
> The session support described here covers strict ordering of messages per entity. It does **not** include session state (saga/workflow patterns). That is a deliberate decision: session state is purely additive and can be layered on top without any breaking changes when a concrete use case arises. See [Future Extension — Saga / Workflow State](#future-extension--saga--workflow-state) below.

### What Sessions Are For

Azure Service Bus sessions give you **strict FIFO ordering _per group_** while still allowing **parallel processing _across groups_**.

Without sessions, messages are dispatched concurrently to any available consumer instance. With sessions, all messages that share the same `SessionId` are delivered to **one consumer at a time, in order**. Different `SessionId` values are processed concurrently.

```
Session "customer-A":  msg1 → msg2 → msg3   (serial, in order)
Session "customer-B":  msg1 → msg2           (serial, in order)
↑ both sessions run concurrently ↑
```

### Step 1 — Implement `ISessionedEvent` on the Integration Event

Add `: ISessionedEvent` to the event record and expose a `SessionId` property that returns the **stable domain key** that groups related messages:

```csharp
[QueueRoute("payment-commands")]
public sealed record PaymentCommand(Guid Id, DateTime OccurredOnUtc, string CustomerId, decimal Amount)
    : IntegrationEvent(Id, OccurredOnUtc), ISessionedEvent
{
    // All payments for the same customer are ordered. Different customers run concurrently.
    public string SessionId => CustomerId;
}
```

The publisher reads `ISessionedEvent.SessionId` automatically and sets it on the outgoing `ServiceBusMessage` — no changes needed at the call site of `IEventPublisher.PublishAsync`.

### Step 2 — Add `RequireSession = true` to the Consumer

```csharp
// Queue session consumer
[ServiceBusQueue(RequireSession = true)]
public class PaymentCommandConsumer : ServiceBusConsumer<PaymentCommand>
{
    protected override async Task ProcessMessageAsync(
        PaymentCommand message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        // All messages for the same CustomerId arrive here in strict order.
        // Messages for different CustomerIds are processed concurrently.
        string customerId = originalMessage.SessionId;
        await ProcessPaymentInOrder(customerId, message, cancellationToken);
    }
}

// Topic subscription session consumer — identical, just a different attribute
[ServiceBusTopic("fulfillment-subscription", RequireSession = true)]
public class OrderFulfillmentConsumer : ServiceBusConsumer<OrderPlacedEvent>
{
    protected override async Task ProcessMessageAsync(
        OrderPlacedEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        string orderId = originalMessage.SessionId;
        await FulfillOrder(orderId, message, cancellationToken);
    }
}
```

### Step 3 — Provision the Queue / Subscription with Sessions Enabled

Sessions must be enabled **at the Azure Service Bus entity level** — this cannot be changed after creation.

```bicep
resource queue 'Microsoft.ServiceBus/namespaces/queues@2022-01-01-preview' = {
  properties: {
    requiresSession: true      // ← must match RequireSession = true on the consumer
    maxDeliveryCount: 100      // high sentinel — application attribute owns the retry budget
  }
}
```

> ⚠️ `requiresSession` cannot be changed after the entity is created. A session-enabled queue rejects messages that have no `SessionId` set, and can only be consumed via a session-aware processor.

### How It Works Under the Hood

| Concern | Detail |
|---|---|
| **Processor type** | `RequireSession = true` → `ServiceBusSessionProcessor`; `false` → `ServiceBusProcessor` |
| **Concurrency** | Controlled by `MaxConcurrentSessions` (default `8`) and `MaxConcurrentCallsPerSession` (default `1`) |
| **Ordering guarantee** | `MaxConcurrentCallsPerSession = 1` is what enforces serial processing within a session — do not increase this |
| **Lock renewal** | Session lock is auto-renewed for up to `MaxAutoLockRenewalDuration` (5 minutes) while a message is being processed |
| **Publisher** | `ServiceBusPublisher` detects `ISessionedEvent` and sets `ServiceBusMessage.SessionId` automatically |
| **Contract validation** | At startup, `ValidateSessionContract` throws `InvalidOperationException` if `RequireSession = true` but the message type does not implement `ISessionedEvent`, and vice versa — misconfiguration is caught before any message is processed |

### Choosing a `SessionId`

The `SessionId` is the **aggregate root ID** for messages that must be ordered relative to each other.

**✅ Good candidates**

| Use case | `SessionId` | Why |
|---|---|---|
| Payment processing | `CustomerId` | Payments per customer must not interleave |
| Order lifecycle | `OrderId` | `Created → Paid → Shipped` must arrive in order |
| Inventory updates | `ProductId` | Stock level changes must be sequential |
| IoT telemetry | `DeviceId` | Readings per device must be ordered |
| Ledger entries | `AccountId` | Debits/credits must not be reordered |

**❌ Bad candidates**

| Choice | Problem |
|---|---|
| `event.Id` (unique Guid per message) | Every message is its own session — ordering overhead with zero ordering benefit |
| New `Guid.NewGuid()` | Same as above |
| A constant string (e.g. `"global"`) | All messages queue behind one session — single-threaded globally, throughput collapses |
| An overly broad key (e.g. tenant ID in a single-tenant system) | Creates a hot session; one session handles everything sequentially |

> 💡 **Rule of thumb:** if messages for entity A and entity B don't care about their order relative to each other, they should be different sessions. If messages for entity A must be ordered among themselves, they share a `SessionId`.

### Concurrency Tuning for Sessions

```json
{
  "EventBusOptions": {
    "MaxConcurrentSessions": 8,
    "MaxConcurrentCallsPerSession": 1
  }
}
```

| Setting | Meaning | Guidance |
|---|---|---|
| `MaxConcurrentSessions` | How many distinct sessions are processed in parallel | Increase for higher throughput when many entities are active simultaneously (16–32 is common for I/O-bound workloads) |
| `MaxConcurrentCallsPerSession` | Concurrent calls within a single session | **Keep at `1`** — anything higher breaks the FIFO ordering guarantee |

### Future Extension — Saga / Workflow State

Azure Service Bus sessions optionally support **per-session state** (up to 256 KB of arbitrary bytes per session). This enables stateful saga/workflow patterns where a consumer can persist and resume partial progress across messages in the same session — for example, tracking which steps of an order saga have been completed.

**This is not implemented yet** — it was deliberately left out because no use case requires it today. When needed, it can be added as a purely additive change (a new scoped `ISessionContext` service injected via DI) with **zero changes** to the current `IServiceBusConsumer`, `ServiceBusConsumer<T>`, or any existing consumer.

The rough extension point:

```csharp
// Application layer abstraction (no Azure SDK dependency)
public interface ISessionContext
{
    string SessionId { get; }
    Task<BinaryData?> GetStateAsync(CancellationToken cancellationToken = default);
    Task SetStateAsync(BinaryData? state, CancellationToken cancellationToken = default);
}

// Saga consumer — injects ISessionContext like any other service
[ServiceBusQueue("order-saga-commands", RequireSession = true)]
public class OrderSagaConsumer(ISessionContext sessionContext) : ServiceBusConsumer<OrderSagaCommand>
{
    protected override async Task ProcessMessageAsync(
        OrderSagaCommand message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        BinaryData? raw = await sessionContext.GetStateAsync(cancellationToken);
        OrderSagaState state = raw is not null
            ? JsonSerializer.Deserialize<OrderSagaState>(raw)!
            : new OrderSagaState();

        state.Apply(message);

        await sessionContext.SetStateAsync(
            new BinaryData(JsonSerializer.SerializeToUtf8Bytes(state)),
            cancellationToken);
    }
}
```

> ⚠️ If saga state exceeds 256 KB, store it in a database row or blob keyed by `SessionId` instead of relying on the Service Bus session state directly.

---

## 🏷️ Consumer Attribute Reference

Every consumer must be decorated with either `[ServiceBusQueue]` or `[ServiceBusTopic]`.

### ServiceBusQueueAttribute

```csharp
// Recommended — queue name resolved from [QueueRoute] on the message type:
[ServiceBusQueue(RequireSession = false, MaxDeliveryCount = 5)]

// Override — explicit name takes precedence over [QueueRoute]:
[ServiceBusQueue("explicit-queue-name", RequireSession = false, MaxDeliveryCount = 5)]
```

| Property | Type | Default | Description |
|---|---|---|---|
| `queueName` *(optional)* | `string` | `null` — resolved from `[QueueRoute]` | Explicit queue name override. When omitted the name is read from `[QueueRoute]` on the message type. |
| `RequireSession` | `bool` | `false` | Use a session-aware processor (FIFO per `SessionId`). Queue must have sessions enabled in Azure. |
| `MaxDeliveryCount` | `int` | `5` | Dead-letter after N delivery attempts. `0` = disable code-level enforcement and rely on the Azure entity's setting. |

### ServiceBusTopicAttribute

```csharp
// Topic name resolved from [TopicRoute] on the message type; only subscription name is required:
[ServiceBusTopic("subscription-name", RequireSession = false, MaxDeliveryCount = 5)]
```

| Property | Type | Default | Description |
|---|---|---|---|
| `subscriptionName` | `string` | *(required)* | This consumer's subscription name. Subscription names are consumer-specific — a topic can have many independent subscribers. |
| `RequireSession` | `bool` | `false` | Use a session-aware processor (FIFO per `SessionId`). Subscription must have sessions enabled in Azure. |
| `MaxDeliveryCount` | `int` | `5` | Dead-letter after N delivery attempts. `0` = disable code-level enforcement and rely on the Azure entity's setting. |

> 📌 The topic name is **not** a parameter of `[ServiceBusTopic]`. It is always resolved from `[TopicRoute("topic-name")]` on the message type. This ensures the topic name is defined exactly once.

### MaxDeliveryCount — Design Intent

Rather than splitting the retry budget across two places (code + Azure entity), this framework treats the **attribute as the sole source of truth**.

To make this work cleanly, all Azure Service Bus queues and topic subscriptions must be provisioned with a **high entity-level `MaxDeliveryCount`** (e.g. `100`) in Bicep/ARM so the broker never dead-letters a message before the application code reaches the threshold:

```bicep
resource queue 'Microsoft.ServiceBus/namespaces/queues@2022-01-01-preview' = {
  properties: {
    maxDeliveryCount: 100  // High sentinel — application code owns the retry budget
  }
}
```

This means:
- The attribute value is the **only** retry number you ever think about
- No risk of Azure entity and attribute values drifting out of sync
- Change the retry budget in a code PR — no infrastructure change required

```
Azure entity MaxDeliveryCount = 100  ← provisioned once, never changes per consumer
[ServiceBusQueue(MaxDeliveryCount = 5)]  ← only this matters ✅
```

> ⚠️ **If you set `MaxDeliveryCount = 0`** (disable code-level enforcement), the Azure entity's value takes over. Ensure the entity is configured to the value you actually want — this is the only case where you need to coordinate both places.

### How Dead-Lettering Works

On every failed attempt the framework checks `message.DeliveryCount` against `MaxDeliveryCount`:

```
Attempt 1 fails → DeliveryCount (1) < MaxDeliveryCount → Abandon  → re-queued
Attempt 2 fails → DeliveryCount (2) < MaxDeliveryCount → Abandon  → re-queued
Attempt N fails → DeliveryCount (N) >= MaxDeliveryCount → Dead-letter ✅
```

### Examples

```csharp
// Minimal — queue name from [QueueRoute] on PaymentEvent, default MaxDeliveryCount of 5
[ServiceBusQueue]
public class PaymentConsumer : ServiceBusConsumer<PaymentEvent> { ... }

// Override retry limit — dead-letter after 3 failed attempts
[ServiceBusQueue(MaxDeliveryCount = 3)]
public class PaymentConsumer : ServiceBusConsumer<PaymentEvent> { ... }

// Disable code-level enforcement — rely entirely on the Azure entity's MaxDeliveryCount
[ServiceBusQueue(MaxDeliveryCount = 0)]
public class PaymentConsumer : ServiceBusConsumer<PaymentEvent> { ... }

// Session-aware with custom retry limit
[ServiceBusQueue(RequireSession = true, MaxDeliveryCount = 5)]
public class OrderedPaymentConsumer : ServiceBusConsumer<PaymentCommand> { ... }

// Topic subscription — topic name from [TopicRoute] on OrderPlacedEvent
[ServiceBusTopic("payments-subscription", MaxDeliveryCount = 3)]
public class OrderPaymentConsumer : ServiceBusConsumer<OrderPlacedEvent> { ... }

// Explicit queue name override (e.g. consuming an event owned by another module)
[ServiceBusQueue("legacy-payment-queue", MaxDeliveryCount = 3)]
public class LegacyPaymentConsumer : ServiceBusConsumer<PaymentEvent> { ... }
```

---

## 🔑 Authentication & Connection

The `ServiceBusClient` is created automatically from the `EventBusOptions` section in `appsettings.json`.  
Three authentication strategies are supported — choose the one that matches your environment.

### Option A — Connection String *(Development / CI)*

Use a full Azure Service Bus connection string (SAS key).  
Suitable for local development with the [Azure Service Bus Emulator](https://learn.microsoft.com/azure/service-bus-messaging/overview-emulator) or a shared dev namespace.

```json
{
  "EventBusOptions": {
    "AuthenticationType": "ConnectionString",
    "ConnectionString": {
      "ConnectionString": "Endpoint=sb://my-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<key>"
    }
  }
}
```

**Local emulator example** (`appsettings.Development.json`):

```json
{
  "EventBusOptions": {
    "AuthenticationType": "ConnectionString",
    "ConnectionString": {
      "ConnectionString": "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
    },
    "MaxConcurrentCalls": 5,
    "MaxConcurrentSessions": 4,
    "MaxConcurrentCallsPerSession": 1
  }
}
```

---

### Option B — Managed Identity *(Staging / Production — recommended)*

Uses an [Azure Managed Identity](https://learn.microsoft.com/azure/active-directory/managed-identities-azure-resources/overview) — no secrets stored anywhere.

**System-assigned identity** (omit `ClientId`):

```json
{
  "EventBusOptions": {
    "AuthenticationType": "ManagedIdentity",
    "ManagedIdentity": {
      "FullyQualifiedNamespace": "my-namespace.servicebus.windows.net"
    }
  }
}
```

**User-assigned identity** (provide `ClientId`):

```json
{
  "EventBusOptions": {
    "AuthenticationType": "ManagedIdentity",
    "ManagedIdentity": {
      "FullyQualifiedNamespace": "my-namespace.servicebus.windows.net",
      "ClientId": "00000000-0000-0000-0000-000000000000"
    }
  }
}
```

> The identity must have the **Azure Service Bus Data Owner** (or a scoped Sender/Receiver) role on the namespace.

#### Local Development with Managed Identity (User Account)


For local development, you have two supported options:

**A. Use a Service Bus Connection String (Recommended for Local):**

```json
{
  "EventBusOptions": {
    "AuthenticationType": "ConnectionString",
    "ConnectionString": {
      "ConnectionString": "Endpoint=sb://my-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<key>"
    }
  }
}
```

**B. Use Managed Identity with your Azure user account:**

```json
{
  "EventBusOptions": {
    "AuthenticationType": "ManagedIdentity",
    "ManagedIdentity": {
      "FullyQualifiedNamespace": "my-namespace.servicebus.windows.net"
    }
  }
}
```

- `FullyQualifiedNamespace` is required (e.g., `my-namespace.servicebus.windows.net`, no `https://`).
- Omit `ClientId` for local user authentication.

**How it works:**
- The Azure SDK uses your local developer credentials (from `az login`, Visual Studio, or Azure CLI) when no `ClientId` is specified.
- Your Azure user account must have the appropriate Azure RBAC role (e.g., Azure Service Bus Data Sender/Receiver) on the Service Bus resource.

**Steps:**
1. Log in to Azure locally using `az login` or ensure you are signed in with Visual Studio.
2. Ensure your user has the correct RBAC role assigned in the Azure Portal.
3. Run your application with the above configuration.

> **Note:**
> - Do **not** use `"AuthenticationType": "AzureCli"` — this is not a supported value. The SDK will use your Azure CLI credentials automatically if you use `ManagedIdentity` with no `ClientId`.
> - If you see errors about `169.254.169.254:80` being unreachable, ensure you are not specifying a `ClientId` for local development, and that you are not using `ManagedIdentity` in an environment that does not support it (such as a local machine without Azure CLI/VS login).
> - For production, use a system- or user-assigned managed identity, or an Azure AD application registration.

---

### Option C — App Registration / Service Principal *(Automation / non-Azure environments)*

Uses [client credentials](https://learn.microsoft.com/azure/active-directory/develop/v2-oauth2-client-creds-grant-flow) (Azure AD App Registration).  
Suitable for CI pipelines, on-premises deployments, or any environment that cannot use Managed Identity.

```json
{
  "EventBusOptions": {
    "AuthenticationType": "ApplicationRegistration",
    "ApplicationRegistration": {
      "FullyQualifiedNamespace": "my-namespace.servicebus.windows.net",
      "TenantId": "00000000-0000-0000-0000-000000000000",
      "ClientId": "00000000-0000-0000-0000-000000000000",
      "ClientSecret": "<secret>"
    }
  }
}
```

> Store `ClientSecret` in Azure Key Vault or a secrets manager — **never commit it to source control**.

---

### Processor Tuning (all auth types)

These optional settings control concurrency and can be added to any configuration:

```json
{
  "EventBusOptions": {
    "AuthenticationType": "...",
    "MaxConcurrentCalls": 10,
    "MaxConcurrentSessions": 8,
    "MaxConcurrentCallsPerSession": 1
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxConcurrentCalls` | `10` | Max concurrent message processing operations (non-session) |
| `MaxConcurrentSessions` | `8` | Max concurrent sessions (session-aware processors) |
| `MaxConcurrentCallsPerSession` | `1` | Max concurrent calls per session |

**Environment-specific tuning:**

```json
// appsettings.Development.json
{ "EventBusOptions": { "MaxConcurrentCalls": 5 } }

// appsettings.Production.json
{ "EventBusOptions": { "MaxConcurrentCalls": 20 } }
```

---

### Options Model

The full C# model that the configuration binds to:

```csharp
public sealed class EventBusOptions
{
    public EventBusAuthenticationType AuthenticationType { get; set; }

    public ConnectionStringOptions?      ConnectionString      { get; set; }
    public ManagedIdentityOptions?       ManagedIdentity       { get; set; }
    public ApplicationRegistrationOptions? ApplicationRegistration { get; set; }

    public int MaxConcurrentCalls          { get; set; } = 10;
    public int MaxConcurrentSessions       { get; set; } = 8;
    public int MaxConcurrentCallsPerSession { get; set; } = 1;
}

public enum EventBusAuthenticationType
{
    ConnectionString,        // SAS key / emulator
    ManagedIdentity,         // system- or user-assigned MI
    ApplicationRegistration  // Azure AD client-credentials
}
```

If the wrong sub-section is missing (e.g. `AuthenticationType = ManagedIdentity` but no `ManagedIdentity` block), the application throws a descriptive `InvalidOperationException` at startup.

---

## 🎛️ Global ServiceBus Settings

> Processor concurrency settings have been consolidated into `EventBusOptions`.  
> See [Authentication & Connection → Processor Tuning](#processor-tuning-all-auth-types) for the full reference.

```json
{
  "EventBusOptions": {
    "MaxConcurrentCalls": 10,
    "MaxConcurrentSessions": 8,
    "MaxConcurrentCallsPerSession": 1
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxConcurrentCalls` | `10` | Max concurrent message processing operations |
| `AutoCompleteMessages` | `false` | Manual completion (more control) — not configurable |
| `MaxAutoLockRenewalDuration` | `5 minutes` | Auto-renew locks during processing — not configurable |

---

## 💉 Dependency Injection

### Registration (already done in `AddInfrastructureCommonServices`)

```csharp
services.AddSingleton<ServiceBusClient>(...);
services.AddSingleton<IEventPublisher, ServiceBusPublisher>();   // publisher
services.AddSingleton(new ServiceBusAssemblyRegistry(consumerAssemblies));
services.AddHostedService<ServiceBusProcessorBackgroundService>();  // consumer background service
```

Consumers are **auto-discovered** via reflection at startup — no manual registration required.

### Consumer Scope Lifecycle

Each message gets its **own DI scope**. Scoped services are safe to inject:

```csharp
[ServiceBusQueue]
public class MyConsumer(
    ApplicationDbContext dbContext,     // ✅ scoped — safe
    ISender sender,                     // ✅ scoped — safe
    IMyService myService                // ✅ transient/scoped — safe
) : ServiceBusConsumer<MyEvent>
{
    protected override async Task ProcessMessageAsync(
        MyEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        await myService.DoSomethingAsync(message.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        // scope + all services disposed automatically after this returns
    }
}
```

---

## 📊 OpenTelemetry Observability

All publish and consume operations are **automatically traced** with no extra code needed.

### Spans Created

| Span Name | Kind | When |
|-----------|------|------|
| `ServiceBus:Publish {queue}` | `Producer` | Every `IEventPublisher.PublishAsync` call |
| `ServiceBus:Startup` | — | Application startup |
| `ServiceBus:Setup:{ConsumerType}` | — | Per consumer at startup |
| `ServiceBus:Process:{ConsumerType}` | `Consumer` | Per message received |
| `ServiceBus:Error` | — | Processor-level error |
| `ServiceBus:Setup:Error` | — | Consumer setup failure |

### Distributed Trace — End-to-End

`ServiceBusPublisher` injects `traceparent` into `ApplicationProperties`. The consumer reads it and links back. This connects both spans into a single distributed trace:

```
[POST /users]
  └── [ServiceBus:Publish user-created-events]       ← SpanKind.Producer
          └── [ServiceBus:Process UserCreatedConsumer]  ← SpanKind.Consumer
                  └── [DbCommand: INSERT ...]
```

Without this, the publisher and consumer spans appear as **completely separate, unrelated traces**.

### Publisher Span Attributes

```
messaging.system              = "azureservicebus"
messaging.operation           = "publish"
messaging.destination.name    = "queue-or-topic-name"
messaging.message.id          = "event-guid"
messaging.message.body.size   = size-in-bytes
```

### Consumer Span Attributes

```
messaging.system                        = "azureservicebus"
messaging.operation                     = "process"
messaging.destination.name              = "queue-or-topic-name"
messaging.message.id                    = "message-id"
messaging.message.body.size             = size-in-bytes
messaging.message.correlation_id        = "correlation-id"   (if present)
messaging.servicebus.delivery_count     = delivery-count
messaging.servicebus.subscription.name  = "subscription"     (topics only)
messaging.consumer.name                 = "ConsumerClassName"
messaging.trace.parent                  = "traceparent"      (if present)
```

### Span Events

| Event | Span |
|-------|------|
| `message.processing.started` | Consumer process span |
| `message.completed` | Consumer process span |
| `message.abandoned` | Consumer process span (on error, will be retried) |
| `message.deadlettered` | Consumer process span (on error, `MaxDeliveryCount` reached) |
| `consumer.started` | Consumer setup span |

### Example Queries

```
# Slow message processing
span.name = "ServiceBus:Process:*" AND span.duration > 5s

# Retry issues
span.attributes["messaging.servicebus.delivery_count"] > 3

# Track a specific event end-to-end (publisher → consumer)
span.attributes["messaging.message.id"] = "your-event-guid"
```

---

## 🔄 Message Processing Patterns

### Pattern 1: MediatR (RECOMMENDED) ✅

```csharp
[ServiceBusQueue]
public class OrderCreatedConsumer(ISender sender) : ServiceBusConsumer<OrderCreatedIntegrationEvent>
{
    protected override async Task ProcessMessageAsync(
        OrderCreatedIntegrationEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        await sender.Send(new ProcessOrderCommand(message.OrderId), cancellationToken);
    }
}
```

### Pattern 2: Direct Database Operations

```csharp
[ServiceBusQueue]
public class OrderStatusConsumer(ApplicationDbContext dbContext) : ServiceBusConsumer<OrderStatusEvent>
{
    protected override async Task ProcessMessageAsync(
        OrderStatusEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        Order? order = await dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == message.OrderId, cancellationToken);

        if (order is null) return; // permanent — complete message

        order.Status = message.Status;
        await dbContext.SaveChangesAsync(cancellationToken);
        // DbContext disposed automatically after this returns
    }
}
```

### Pattern 3: Accessing Message Metadata

```csharp
[ServiceBusQueue]
public class MetadataConsumer : ServiceBusConsumer<MyEvent>
{
    protected override async Task ProcessMessageAsync(
        MyEvent message,
        ServiceBusReceivedMessage originalMessage,  // ← Service Bus metadata here
        CancellationToken cancellationToken)
    {
        // ✅ Good: idempotency check using MessageId
        if (originalMessage.ApplicationProperties.TryGetValue("EventType", out object? eventType))
        {
            // use custom property for routing or idempotency decisions...
        }

        // ❌ Don't use DeliveryCount here to limit retries
        //    → Use MaxDeliveryCount on the attribute instead (see Attribute Reference)
    }
}
```

### Pattern 4: Custom JSON Deserialization

```csharp
[ServiceBusQueue]
public class CustomSerializationConsumer : ServiceBusConsumer<MyComplexEvent>
{
    // Override to customise deserialization
    protected override JsonSerializerOptions JsonSerializerOptions => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    protected override async Task ProcessMessageAsync(
        MyComplexEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        // message already deserialized with custom options above
    }
}
```

---

## 🚨 Error Handling Strategies

### Throw vs Don't Throw

```
✅ SUCCESS — no exception thrown
  → Message completed and removed from queue

⚠️ TRANSIENT ERROR — throw exception
  → Message abandoned, returns to queue for retry
  → Dead-lettered after MaxDeliveryCount attempts (attribute or Azure entity setting)

🛑 PERMANENT ERROR — catch and don't re-throw
  → Message completed and removed (no retry)
```

### Strategy 1: Transient vs Permanent

```csharp
[ServiceBusQueue]
public class PaymentConsumer(ILogger<PaymentConsumer> logger) : ServiceBusConsumer<PaymentEvent>
{
    protected override async Task ProcessMessageAsync(
        PaymentEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await ProcessPayment(message, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw; // transient — network issue, allow retry
        }
        catch (ValidationException ex)
        {
            logger.LogError(ex, "Invalid payment data for event {EventId}", message.Id);
            // permanent — don't throw, complete the message
        }
    }
}
```

### Strategy 2: Limiting Delivery Attempts with `MaxDeliveryCount` ✅ RECOMMENDED

Use the `MaxDeliveryCount` attribute property to dead-letter a message after N failed attempts, **without any code inside the consumer**. The framework handles it automatically.

The attribute is the **sole source of truth** for the retry budget. Azure entities should be provisioned with a high `MaxDeliveryCount` (e.g. `100`) so the broker never interferes — see the [Consumer Attribute Reference](#-consumer-attribute-reference) for the full design rationale.

```csharp
// Dead-letter after 3 failed attempts — zero extra code in the consumer
[ServiceBusQueue(MaxDeliveryCount = 3)]
public class PaymentConsumer : ServiceBusConsumer<PaymentEvent>
{
    protected override async Task ProcessMessageAsync(
        PaymentEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        // If this throws, the framework checks DeliveryCount automatically:
        // Attempt 1 → abandon | Attempt 2 → abandon | Attempt 3 → dead-letter
        await ProcessPayment(message, cancellationToken);
    }
}
```

### Strategy 3: Delivery Count Guard (Business Logic)

Use `originalMessage.DeliveryCount` inside `ProcessMessageAsync` when you need **business-logic decisions** based on how many times a message has been attempted — for example, falling back to a degraded code path on later attempts.

> ℹ️ For simply *limiting total retries*, prefer the `MaxDeliveryCount` attribute (Strategy 2). Use this pattern only when the delivery count needs to influence your processing logic.

```csharp
[ServiceBusQueue]
public class SafeConsumer(ILogger<SafeConsumer> logger) : ServiceBusConsumer<MyEvent>
{
    protected override async Task ProcessMessageAsync(
        MyEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        if (originalMessage.DeliveryCount > 5)
        {
            logger.LogError(
                "Message {MessageId} exceeded max retries. Completing to prevent infinite loop.",
                originalMessage.MessageId);
            return; // complete without retry
        }

        await DoWork(message, cancellationToken);
    }
}
```

### Strategy 4: Idempotency Check

```csharp
[ServiceBusQueue]
public class IdempotentOrderConsumer(ApplicationDbContext dbContext) : ServiceBusConsumer<OrderCreatedEvent>
{
    protected override async Task ProcessMessageAsync(
        OrderCreatedEvent message,
        ServiceBusReceivedMessage originalMessage,
        CancellationToken cancellationToken)
    {
        // Guard against duplicate delivery
        bool alreadyProcessed = await dbContext.ProcessedEvents
            .AnyAsync(e => e.EventId == message.Id, cancellationToken);

        if (alreadyProcessed) return;

        await ProcessOrder(message, cancellationToken);
        dbContext.ProcessedEvents.Add(new ProcessedEvent { EventId = message.Id });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

---

## 🎯 Best Practices

### ✅ DO

- **Annotate every integration event** with `[QueueRoute("...")]` or `[TopicRoute("...")]` — single source of truth
- **Use `[ServiceBusQueue]` without a name** on consumers — the name is resolved from `[QueueRoute]` on the message type
- **Use `ServiceBusRoute.GetQueueName<TEvent>()`** in tests and provisioning tools — stays in sync with the attribute
- **Inject `IEventBusPublisher`** — never `ServiceBusPublisher` or `ServiceBusClient` directly
- **Make operations idempotent** — messages can be delivered more than once
- **Distinguish transient vs permanent errors** — throw for retry, return for complete
- **Use `MaxDeliveryCount` on the attribute** to limit delivery attempts — avoids boilerplate in `ProcessMessageAsync`
- **Use `DeliveryCount` in `ProcessMessageAsync`** only for business-logic decisions (e.g. fallback behaviour on later attempts), not for limiting retries
- **Use MediatR/CQRS** in consumers for clean separation
- **Rely on OpenTelemetry** for infrastructure observability
- **Log business events only** — not MessageId, DeliveryCount, etc. (already in spans)
- **Use `ISessionedEvent` + `RequireSession = true`** when messages for the same entity must be processed in strict order
- **Use a stable domain key as `SessionId`** — e.g. `CustomerId`, `OrderId`, `DeviceId`
- **Provision the Azure entity with `requiresSession: true`** before enabling sessions on a consumer — this cannot be changed after creation

### ❌ DON'T

- **Don't pass the queue name to `PublishAsync`** — define it with `[QueueRoute]` on the event instead
- **Don't hardcode queue/topic name strings** in multiple places — define them once on the event
- **Don't add the topic name to `[ServiceBusTopic]`** — it comes from `[TopicRoute]` on the event
- **Don't inject `ServiceBusClient` directly** in application/domain code
- **Don't log infrastructure details** — already captured in OTel spans
- **Don't forget idempotency** — duplicate delivery is normal
- **Don't make consumers `internal`** — consumer discovery requires `public` classes
- **Don't block threads** — use `async`/`await` throughout
- **Don't use `event.Id` (a unique Guid) as `SessionId`** — every message would be its own session, adding overhead with no ordering benefit
- **Don't set `MaxConcurrentCallsPerSession` > 1** — this breaks the FIFO ordering guarantee within a session
- **Don't send a message without `SessionId` to a session-enabled queue** — the broker will reject it; the startup contract validation catches this at application start

### 🏆 Checklist

- [ ] Integration event inherits `IntegrationEvent` from `BusWorks.Abstractions`
- [ ] Integration event is annotated with `[QueueRoute("...")]` or `[TopicRoute("...")]`
- [ ] Consumer class is `public` with `[ServiceBusQueue]` or `[ServiceBusTopic("subscription-name")]`
- [ ] Consumer inherits `ServiceBusConsumer<TMessage>` or `ServiceBusConsumer`
- [ ] `MaxDeliveryCount` set on the consumer attribute if a tighter retry budget is needed
- [ ] Transient vs permanent errors distinguished
- [ ] Operations are idempotent
- [ ] `IEventBusPublisher` used for publishing (not `ServiceBusClient` directly)
- [ ] Tests use `ServiceBusRoute.GetQueueName<TEvent>()` for DLQ assertions — no hardcoded names
- [ ] End-to-end distributed trace verified in Aspire Dashboard
- [ ] *(Sessions only)* Integration event implements `ISessionedEvent` with a stable domain key as `SessionId`
- [ ] *(Sessions only)* Consumer has `RequireSession = true`
- [ ] *(Sessions only)* Azure entity is provisioned with `requiresSession: true` and a high `MaxDeliveryCount`
- [ ] *(Sessions only)* `MaxConcurrentCallsPerSession` is `1` (default — do not change)

---

## 🔍 Troubleshooting

### Consumer not discovered

- Is the class `public`?
- Does it have `[ServiceBusQueue]` or `[ServiceBusTopic]` attribute?
- Does it inherit from `ServiceBusConsumer` or `ServiceBusConsumer<T>`?
- Is it in an assembly registered via `ServiceBusAssemblyRegistry`?
- Rebuild the solution

### `InvalidOperationException` at startup — missing route attribute

If `[ServiceBusQueue]` has no explicit name, the framework reads `[QueueRoute]` from the message type. If that attribute is missing you will see:

```
Consumer 'MyConsumer' has [ServiceBusQueue] without an explicit queue name,
but message type 'MyEvent' does not have a [QueueRoute] attribute.
Either add [QueueRoute("queue-name")] to 'MyEvent',
or pass the name explicitly: [ServiceBusQueue("queue-name")].
```

Fix: add `[QueueRoute("my-queue")]` to the integration event record.

### `InvalidOperationException` at startup — route/consumer attribute mismatch

If the consumer attribute and the event's route attribute point to different destination types (queue vs topic), the framework detects this at startup with a specific message:

```
// Consumer has [ServiceBusQueue] but event has [TopicRoute]:
Consumer 'ParkEventConsumer' has [ServiceBusQueue] but 'ParkEvent'
is declared as a topic via [TopicRoute("park-events")], not a queue.
Did you mean [ServiceBusTopic("your-subscription-name")] on 'ParkEventConsumer'?

// Consumer has [ServiceBusTopic] but event has [QueueRoute]:
Consumer 'OrderConsumer' has [ServiceBusTopic] but 'OrderCreatedEvent'
is declared as a queue via [QueueRoute("orders")], not a topic.
Did you mean [ServiceBusQueue] on 'OrderConsumer'?
```

Fix: make the consumer attribute match what the event declares — `[ServiceBusQueue]` for `[QueueRoute]` events, `[ServiceBusTopic("sub")]` for `[TopicRoute]` events.

### `ServiceBus:Setup:Error` span at startup

- Does the queue/topic exist in Azure Service Bus / emulator?
- Is `ConnectionStrings:ServiceBus` correct in `appsettings.json`?

### Publish span has no child consumer span (broken distributed trace)

- Verify `ServiceBusPublisher` is injecting `traceparent` into `ApplicationProperties` (it does by default)
- Verify the consumer span is reading `ApplicationProperties["traceparent"]` (it does by default)
- Confirm both publisher and consumer are reporting to the same OTel collector

### Messages being retried infinitely

- Don't re-throw for permanent errors (validation failures, not-found records, etc.)
- Set `MaxDeliveryCount` on the `[ServiceBusQueue]` or `[ServiceBusTopic]` attribute to dead-letter after N attempts automatically
- Ensure Azure entities are provisioned with a high `MaxDeliveryCount` (e.g. `100`) so the broker doesn't dead-letter before the application threshold is reached — see [Consumer Attribute Reference](#-consumer-attribute-reference)
- Or add a `DeliveryCount > N` guard inside `ProcessMessageAsync` for business-logic-driven decisions
- Check the dead-letter queue in the Azure Service Bus portal

### `InvalidOperationException` at startup — session contract mismatch

The framework validates the session contract at startup. You will see one of two errors:

```
// RequireSession = true but the message type does not implement ISessionedEvent:
Consumer 'PaymentCommandConsumer' has RequireSession = true, but message type
'PaymentCommand' does not implement ISessionedEvent.
Add ': ISessionedEvent' to 'PaymentCommand' and expose a SessionId property
that returns a stable domain key (e.g. customerId, orderId).

// Message implements ISessionedEvent but consumer does not have RequireSession = true:
Message type 'PaymentCommand' implements ISessionedEvent (declares a SessionId),
but consumer 'PaymentCommandConsumer' does not have RequireSession = true.
Either add RequireSession = true to the consumer attribute,
or remove ISessionedEvent from 'PaymentCommand'.
```

Fix: ensure both sides agree — `ISessionedEvent` on the event **and** `RequireSession = true` on the consumer, or neither.

### Session messages not received / consumer appears idle

- Verify the Azure entity has `requiresSession: true` — a session-enabled queue cannot be consumed by a non-session processor (and vice versa)
- Verify the publisher is setting `SessionId` on outgoing messages — it does this automatically when the event implements `ISessionedEvent`; check that the event's `SessionId` property returns a non-null, non-empty string
- Verify `RequireSession = true` is on the consumer attribute
- Check `MaxConcurrentSessions` — if it is too low and all session slots are held by long-running sessions, new sessions queue up

### Session messages processing out of order

- Verify `MaxConcurrentCallsPerSession` is `1` (the default) — any higher value allows parallel calls within one session, breaking FIFO
- Verify only one consumer instance exists for the session queue — multiple competing consumers on the same session-enabled queue is valid (the broker assigns each session to one consumer at a time), but check that no separate non-session processor is also attached to the same queue

---

## 📚 Additional Resources

- **Example consumers:** `ExampleServiceBusConsumer.cs`
- **Consumer implementation:** `ServiceBusProcessorBackgroundService.cs`
- **Publisher implementation:** `ServiceBusPublisher.cs`
- **Publisher interface:** `BusWorks.Abstractions/IEventPublisher.cs`
- **Route attributes:** `BusWorks.Abstractions/RouteAttributes.cs`
- **Route helper:** `BusWorks.Abstractions/RouteHelper.cs`
- **Session interface:** `BusWorks.Abstractions/ISessionedEvent.cs`
- **Azure Service Bus Docs:** [Microsoft Learn](https://learn.microsoft.com/azure/service-bus-messaging/)
- **Azure Service Bus Sessions:** [Session documentation](https://learn.microsoft.com/azure/service-bus-messaging/message-sessions)
- **OpenTelemetry Messaging Conventions:** [Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/messaging/)

---

**Last Updated:** March 28, 2026
**Files:** `EvenBusOptions.cs`, `ServiceBusPublisher.cs`, `ServiceBusProcessorBackgroundService.cs`, `IEventBusPublisher.cs`, `ServiceBusRouteAttributes.cs`, `ServiceBusRoute.cs`, `IntegrationEvent.cs`, `ISessionedEvent.cs`
