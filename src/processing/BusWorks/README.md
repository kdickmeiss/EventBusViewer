# BusWorks Guide

Comprehensive guide for publishing and consuming messages using BusWorks — a processor for Azure Service Bus messages that hosts a background service for message consumption and publishing, and is extensible for other brokers.

## 📚 Table of Contents

1. [Architecture Overview](#-architecture-overview)
2. [Publishing Messages](#-publishing-messages)
3. [Consuming Messages](#-consuming-messages)
4. [Topic Subscriptions](#-topic-subscriptions)
5. [Sessions & FIFO Ordering](#-sessions--fifo-ordering)
6. [Consumer Attribute Reference](#-consumer-attribute-reference)
7. [Authentication & Connection](#-authentication--connection)
8. [Global ServiceBus Settings](#-global-servicebus-settings)
9. [Dependency Injection](#-dependency-injection)
10. [OpenTelemetry Observability](#-opentelemetry-observability)
11. [Message Processing Patterns](#-message-processing-patterns)
12. [Error Handling Strategies](#-error-handling-strategies)
13. [Best Practices](#-best-practices)
14. [Troubleshooting](#-troubleshooting)

---

## 🏗️ Architecture Overview

BusWorks follows Clean Architecture — the contracts live in `BusWorks.Abstractions` (Application layer), the implementation lives in `BusWorks` (Infrastructure layer). Consumer classes belong in the Application layer and have **zero** Azure SDK dependencies.

```
BusWorks.Abstractions/              ← framework contracts — zero NuGet dependencies
    IEventBusPublisher.cs
    IIntegrationEvent.cs
    IntegrationEvent.cs
    ISessionedEvent.cs
    ServiceBusRoute.cs
    Attributes/
        ServiceBusRouteAttributes.cs  ← [QueueRoute] / [TopicRoute]
        ServiceBusQueueAttribute.cs   ← [ServiceBusQueue]
        ServiceBusTopicAttribute.cs   ← [ServiceBusTopic]
    Consumer/
        IConsumer.cs                  ← IConsumer<T> + IConsumeContext<T>
        MessageContext.cs             ← broker-agnostic message metadata

BusWorks/                           ← Infrastructure layer — Azure SDK, DI wiring
    BackgroundServices/
        ServiceBusProcessorBackgroundService.cs
    Consumer/
        ConsumeContext.cs
    Options/
        EventBusOptions.cs
    Publisher/
        ServiceBusPublisher.cs
    DependencyInjection.cs
    ServiceBusAssemblyRegistry.cs
```

The above is the **framework**. In your own solution the recommended project layout is:

```
MyService.IntegrationEvents/        ← shared contracts classlib
    ↳ references: BusWorks.Abstractions only
    OrderCreatedEvent.cs            ← [QueueRoute("orders")] record
    PaymentProcessedEvent.cs        ← [QueueRoute("payments")] record
    ...

MyService.Application/              ← Application layer
    ↳ references: BusWorks.Abstractions, MyService.IntegrationEvents
    Consumers/
        OrderCreatedConsumer.cs     ← implements IConsumer<OrderCreatedEvent>
    ...

MyService.Infrastructure/           ← Infrastructure / startup
    ↳ references: BusWorks, MyService.Application, MyService.IntegrationEvents
    Program.cs                      ← services.AddEventBus(..., typeof(OrderCreatedConsumer).Assembly)
```

### Why split across layers?

The Application layer defines **what** (consumer logic, publishing contract). The Infrastructure layer defines **how** (Azure Service Bus, DI wiring). Integration events live in their own classlib so **any service** can reference the shared contracts without pulling in consumers or infrastructure.

#### Integration Events — separate classlib

Integration events are **shared contracts between services**. Putting them in a dedicated `*.IntegrationEvents` project means:

- A **sender service** references `MyService.IntegrationEvents` to publish the event
- A **consumer service** references `MyService.IntegrationEvents` to receive it
- Neither needs to reference the other service's application or infrastructure code
- The classlib only depends on `BusWorks.Abstractions` — no Azure SDK, no business logic

```
OrderService.IntegrationEvents      ← published as NuGet or referenced directly
    [QueueRoute("orders")]
    record OrderCreatedEvent        ← both services share this type
         ↓                                   ↓
  OrderService (publishes)          NotificationService (consumes)
  IEventBusPublisher.PublishAsync   IConsumer<OrderCreatedEvent>
```

#### The concrete consumer class lives in the Application layer

This surprises people at first, but it follows the same logic as MediatR: an `IRequestHandler<T>` lives in Application, not Infrastructure, because it contains use-case logic. The same applies here.

The key reason it stays in Application: **a consumer class has zero Azure SDK dependency**. The `[ServiceBusQueue]` attribute comes from `BusWorks.Abstractions` (no NuGet packages). `IConsumeContext<T>` exposes a broker-agnostic `MessageContext`. The consumer never touches Azure.

```
┌────────────────────────────────────────────────────────────────────────┐
│ MyService.IntegrationEvents  (shared contracts classlib)               │
│                                                                        │
│  [QueueRoute("orders")]                                                │
│  record OrderCreatedEvent : IntegrationEvent                           │
└────────────────────────────────────────────────────────────────────────┘
          ↑ referenced by both sender and consumer services

┌────────────────────────────────────────────────────────────────────────┐
│ MyService.Application  (Application layer)                             │
│                                                                        │
│  [ServiceBusQueue]                   ← routing declaration only        │
│  class OrderCreatedConsumer          ← use-case logic lives here       │
│      : IConsumer<OrderCreatedEvent>                                    │
│  {                                                                     │
│      // zero Azure SDK — context.Metadata is broker-agnostic          │
│      public Task Consume(IConsumeContext<OrderCreatedEvent> context)   │
│  }                                                                     │
│                                                                        │
│  IEventBusPublisher                  ← publishing contract             │
└────────────────────────────────────────────────────────────────────────┘
                    ↑ discovers IConsumer<T> classes
                    ↑ implements IEventBusPublisher
┌────────────────────────────────────────────────────────────────────────┐
│ Infrastructure  (BusWorks)                                             │
│                                                                        │
│  ServiceBusProcessorBackgroundService  ← wires consumers to Azure     │
│  ServiceBusPublisher                   ← publishes via Azure SDK      │
│  DependencyInjection / AddEventBus()                                   │
└────────────────────────────────────────────────────────────────────────┘
```

The `[ServiceBusQueue]` attribute is a **routing declaration** — exactly like `[HttpGet("route")]` on a controller action. Nobody puts a controller in the Infrastructure layer just because it has an HTTP routing attribute. The same principle applies here.

**Publishing follows the same pattern:**

```
Controller / Handler
  → IEventBusPublisher.PublishAsync   (Application — contract)
       ↑ implemented by
  ServiceBusPublisher                  (Infrastructure — Azure SDK, reads [QueueRoute])
```

### Route attributes — single source of truth

The queue or topic name for an integration event is declared **once** on the event record itself via `[QueueRoute]` or `[TopicRoute]`. Both the publisher and the consumer infrastructure read from this attribute automatically.

```
[QueueRoute("resort-created")]   ← defined once, on the event
       ↓                                ↓
IEventBusPublisher.PublishAsync  [ServiceBusQueue] consumer
(reads it automatically)         (reads it automatically)
```

---

## 📤 Publishing Messages

### 1. Define Your Integration Event

Create a dedicated `*.IntegrationEvents` classlib that references only `BusWorks.Abstractions`. This keeps the event contracts completely independent — other services can reference this project (or its NuGet package) to publish or subscribe to the same events without depending on your application or infrastructure code.

```xml
<!-- MyService.IntegrationEvents.csproj -->
<ItemGroup>
    <ProjectReference Include="..\BusWorks.Abstractions\BusWorks.Abstractions.csproj" />
</ItemGroup>
```

Then define your events:

```csharp
// MyService.IntegrationEvents / UserCreatedIntegrationEvent.cs
using BusWorks;
using BusWorks.Attributes;

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
using BusWorks;

public class RegisterUserCommandHandler(IEventBusPublisher eventBus) : ICommandHandler<RegisterUserCommand>
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
    [FromServices] IEventBusPublisher eventBus,
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

### IEventBusPublisher Interface

Defined in `BusWorks.Abstractions`:

```csharp
public interface IEventBusPublisher
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

You never interact with `ServiceBusPublisher` directly — always inject `IEventBusPublisher`.

---

## 📥 Consuming Messages

### Quick Start

Implement `IConsumer<TMessage>`, inject your dependencies via the constructor, and decorate with `[ServiceBusQueue]`. The queue name is resolved automatically from `[QueueRoute]` on the message type:

```csharp
using BusWorks.Attributes;
using BusWorks.Consumer;

// Queue name comes from [QueueRoute("user-created-events")] on UserCreatedIntegrationEvent
[ServiceBusQueue]
public class UserCreatedConsumer(IEmailService emailService) : IConsumer<UserCreatedIntegrationEvent>
{
    public async Task Consume(IConsumeContext<UserCreatedIntegrationEvent> context)
    {
        UserCreatedIntegrationEvent msg = context.Message; // already deserialized ✨
        await emailService.SendWelcomeEmailAsync(msg.Email, context.CancellationToken);
    }
}
```

Consumers are **automatically discovered** at startup — no manual DI registration needed.

> 💡 **Note:** OpenTelemetry tracing is automatic! No infrastructure logging needed (message IDs, delivery counts, etc.). Only log business-critical events.

### IConsumeContext\<T\>

The `context` parameter gives you everything you need inside a consumer:

| Property | Type | Description |
|---|---|---|
| `context.Message` | `TMessage` | The deserialized integration event |
| `context.Metadata` | `MessageContext` | Broker metadata — `MessageId`, `SessionId`, `CorrelationId`, `DeliveryCount`, `SequenceNumber`, `EnqueuedTime`, `ContentType`, `Subject`, `ApplicationProperties` |
| `context.CancellationToken` | `CancellationToken` | Cancellation token for the processing operation |

`MessageContext` is entirely broker-agnostic — no Azure SDK type on your consumer's surface.

### With an Explicit Name Override

Pass a name directly when you need to override, or when the message type has no `[QueueRoute]` attribute:

```csharp
[ServiceBusQueue("user-created-events")]
public class UserCreatedConsumer : IConsumer<UserCreatedIntegrationEvent>
{
    // ...
}
```

The explicit name takes precedence over the route attribute.

### Using ServiceBusRoute in Tests and Tools

When you need the queue name outside of a consumer (e.g. DLQ assertions in tests, provisioning scripts), use `ServiceBusRoute` — it reads from the same `[QueueRoute]` attribute:

```csharp
string queue = ServiceBusRoute.GetQueueName<UserCreatedIntegrationEvent>();

// In an integration test:
IReadOnlyList<ServiceBusReceivedMessage> dlqMessages =
    await EventBus.WaitForDeadLetterMessagesAsync(queue, expectedCount: 1);
```

This keeps everything in sync — rename the queue in one place (`[QueueRoute]`) and it automatically propagates to publishers, consumers, tests, and tools.

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
public class UserEventsEmailConsumer(IEmailService emailService)
    : IConsumer<UserCreatedIntegrationEvent>
{
    public async Task Consume(IConsumeContext<UserCreatedIntegrationEvent> context)
    {
        await emailService.SendWelcomeEmailAsync(context.Message.Email, context.CancellationToken);
    }
}

// Notification Service — subscribes to the SAME topic, different subscription
[ServiceBusTopic("notification-service-subscription")]
public class UserEventsNotificationConsumer(INotificationService notificationService)
    : IConsumer<UserCreatedIntegrationEvent>
{
    public async Task Consume(IConsumeContext<UserCreatedIntegrationEvent> context)
    {
        await notificationService.CreateAsync(context.Message, context.CancellationToken);
    }
}
```

One message published to the topic is delivered to **all subscriptions**. The topic name never appears more than once — on the event record.

### Subscription Name Constants (Optional)

```csharp
public static class SubscriptionNames
{
    public const string EmailService        = "email-service-subscription";
    public const string NotificationService = "notification-service-subscription";
}

[ServiceBusTopic(SubscriptionNames.EmailService)]
public class UserEventsEmailConsumer : IConsumer<UserCreatedIntegrationEvent> { ... }

[ServiceBusTopic(SubscriptionNames.NotificationService)]
public class UserEventsNotificationConsumer : IConsumer<UserCreatedIntegrationEvent> { ... }
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

The publisher reads `ISessionedEvent.SessionId` automatically and sets it on the outgoing `ServiceBusMessage` — no changes needed at the call site of `IEventBusPublisher.PublishAsync`.

### Step 2 — Add `RequireSession = true` to the Consumer

```csharp
// Queue session consumer
[ServiceBusQueue(RequireSession = true)]
public class PaymentCommandConsumer : IConsumer<PaymentCommand>
{
    public async Task Consume(IConsumeContext<PaymentCommand> context)
    {
        // All messages for the same CustomerId arrive here in strict order.
        // Messages for different CustomerIds are processed concurrently.
        string customerId = context.Metadata.SessionId!;
        await ProcessPaymentInOrder(customerId, context.Message, context.CancellationToken);
    }
}

// Topic subscription session consumer — identical, just a different attribute
[ServiceBusTopic("fulfillment-subscription", RequireSession = true)]
public class OrderFulfillmentConsumer : IConsumer<OrderPlacedEvent>
{
    public async Task Consume(IConsumeContext<OrderPlacedEvent> context)
    {
        string orderId = context.Metadata.SessionId!;
        await FulfillOrder(orderId, context.Message, context.CancellationToken);
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

Azure Service Bus sessions optionally support **per-session state** (up to 256 KB of arbitrary bytes per session). This enables stateful saga/workflow patterns where a consumer can persist and resume partial progress across messages in the same session.

**This is not implemented yet** — it was deliberately left out because no use case requires it today. When needed, it can be added as a purely additive change (a new scoped `ISessionContext` service injected via DI) with **zero changes** to `IConsumer<T>` or any existing consumer.

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
public class OrderSagaConsumer(ISessionContext sessionContext) : IConsumer<OrderSagaCommand>
{
    public async Task Consume(IConsumeContext<OrderSagaCommand> context)
    {
        BinaryData? raw = await sessionContext.GetStateAsync(context.CancellationToken);
        OrderSagaState state = raw is not null
            ? JsonSerializer.Deserialize<OrderSagaState>(raw)!
            : new OrderSagaState();

        state.Apply(context.Message);

        await sessionContext.SetStateAsync(
            new BinaryData(JsonSerializer.SerializeToUtf8Bytes(state)),
            context.CancellationToken);
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

On every failed attempt the framework checks `context.Metadata.DeliveryCount` against `MaxDeliveryCount`:

```
Attempt 1 fails → DeliveryCount (1) < MaxDeliveryCount → Abandon  → re-queued
Attempt 2 fails → DeliveryCount (2) < MaxDeliveryCount → Abandon  → re-queued
Attempt N fails → DeliveryCount (N) >= MaxDeliveryCount → Dead-letter ✅
```

### Examples

```csharp
// Minimal — queue name from [QueueRoute] on PaymentEvent, default MaxDeliveryCount of 5
[ServiceBusQueue]
public class PaymentConsumer : IConsumer<PaymentEvent> { ... }

// Override retry limit — dead-letter after 3 failed attempts
[ServiceBusQueue(MaxDeliveryCount = 3)]
public class PaymentConsumer : IConsumer<PaymentEvent> { ... }

// Disable code-level enforcement — rely entirely on the Azure entity's MaxDeliveryCount
[ServiceBusQueue(MaxDeliveryCount = 0)]
public class PaymentConsumer : IConsumer<PaymentEvent> { ... }

// Session-aware with custom retry limit
[ServiceBusQueue(RequireSession = true, MaxDeliveryCount = 5)]
public class OrderedPaymentConsumer : IConsumer<PaymentCommand> { ... }

// Topic subscription — topic name from [TopicRoute] on OrderPlacedEvent
[ServiceBusTopic("payments-subscription", MaxDeliveryCount = 3)]
public class OrderPaymentConsumer : IConsumer<OrderPlacedEvent> { ... }

// Explicit queue name override (e.g. consuming an event owned by another module)
[ServiceBusQueue("legacy-payment-queue", MaxDeliveryCount = 3)]
public class LegacyPaymentConsumer : IConsumer<PaymentEvent> { ... }
```

---

## 🔑 Authentication & Connection

The `ServiceBusClient` is created automatically from the `EventBusOptions` section in `appsettings.json`.  
Four authentication strategies are supported — choose the one that matches your environment.

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

- Omit `ClientId` for local user authentication.
- Your Azure user account must have the appropriate Azure RBAC role on the Service Bus resource.

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

### Option D — Azure CLI *(Local Development — recommended)*

Uses the Azure CLI's logged-in user for authentication.

```json
{
  "EventBusOptions": {
    "AuthenticationType": "AzureCli",
    "AzureCli": {
      "FullyQualifiedNamespace": "my-namespace.servicebus.windows.net"
    }
  }
}
```

**Steps:**
1. Install the Azure CLI and log in using `az login`.
2. Ensure your user has the correct RBAC role assigned in the Azure Portal.
3. Run your application with the above configuration.

---

### Processor Tuning (all auth types)

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

---

### Options Model

```csharp
public sealed class EventBusOptions
{
    public EventBusAuthenticationType AuthenticationType { get; set; }

    public ConnectionStringOptions?        ConnectionString        { get; set; }
    public ManagedIdentityOptions?         ManagedIdentity         { get; set; }
    public ApplicationRegistrationOptions? ApplicationRegistration { get; set; }
    public AzureCliOptions?                AzureCli                { get; set; }

    public int MaxConcurrentCalls           { get; set; } = 10;
    public int MaxConcurrentSessions        { get; set; } = 8;
    public int MaxConcurrentCallsPerSession { get; set; } = 1;
}

public enum EventBusAuthenticationType
{
    ConnectionString,        // SAS key / emulator
    ManagedIdentity,         // system- or user-assigned MI
    ApplicationRegistration, // Azure AD client-credentials
    AzureCli                 // Azure CLI user (local development)
}
```

If the wrong sub-section is missing (e.g. `AuthenticationType = ManagedIdentity` but no `ManagedIdentity` block), the application throws a descriptive `InvalidOperationException` at startup.

---

## 🎛️ Global ServiceBus Settings

> Processor concurrency settings have been consolidated into `EventBusOptions`.  
> See [Authentication & Connection → Processor Tuning](#processor-tuning-all-auth-types) for the full reference.

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxConcurrentCalls` | `10` | Max concurrent message processing operations |
| `AutoCompleteMessages` | `false` | Manual completion (more control) — not configurable |
| `MaxAutoLockRenewalDuration` | `5 minutes` | Auto-renew locks during processing — not configurable |

---

## 💉 Dependency Injection

### Registration

All required services are registered via the `AddEventBus` extension method. Pass the assemblies that contain your consumer classes:

```csharp
// In Program.cs or Startup.cs:
services.AddEventBus(
    configuration,
    typeof(UserCreatedConsumer).Assembly,   // application / consumer assembly
    typeof(OrderConsumer).Assembly          // additional assemblies as needed
);
```

This single call:
- Creates and registers the Azure Service Bus client
- Registers `IEventBusPublisher` → `ServiceBusPublisher`
- Registers `ServiceBusAssemblyRegistry` (scans provided assemblies once at startup)
- **Registers every discovered `IConsumer<T>` as `Scoped`** — no manual registration needed
- Registers the background processor service

### Consumer Scope Lifecycle

Each message gets its **own DI scope**. Because consumers are registered as `Scoped`, all scoped services are safe to inject via the constructor:

```csharp
[ServiceBusQueue]
public class MyConsumer(
    ApplicationDbContext dbContext,     // ✅ scoped — safe
    ISender sender,                     // ✅ scoped — safe
    IMyService myService                // ✅ transient/scoped — safe
) : IConsumer<MyEvent>
{
    public async Task Consume(IConsumeContext<MyEvent> context)
    {
        await myService.DoSomethingAsync(context.Message.Id, context.CancellationToken);
        await dbContext.SaveChangesAsync(context.CancellationToken);
        // scope + all services disposed automatically after this returns
    }
}
```

> **Startup validation:** because consumers are registered in the DI container as `Scoped`, bad constructor dependencies are caught immediately when the application starts — not on the first message.

---

## 📊 OpenTelemetry Observability

All publish and consume operations are **automatically traced** with no extra code needed.

### Spans Created

| Span Name | Kind | When |
|-----------|------|------|
| `ServiceBus:Publish {queue}` | `Producer` | Every `IEventBusPublisher.PublishAsync` call |
| `ServiceBus:Startup` | — | Application startup |
| `ServiceBus:Setup:{ConsumerType}` | — | Per consumer at startup |
| `ServiceBus:Process:{ConsumerType}` | `Consumer` | Per message received |
| `ServiceBus:Error` | — | Processor-level error |
| `ServiceBus:Setup:Error` | — | Consumer setup failure |

### Distributed Trace — End-to-End

`ServiceBusPublisher` injects `traceparent` into `ApplicationProperties`. The consumer reads it and links back:

```
[POST /users]
  └── [ServiceBus:Publish user-created-events]         ← SpanKind.Producer
          └── [ServiceBus:Process UserCreatedConsumer]  ← SpanKind.Consumer
                  └── [DbCommand: INSERT ...]
```

Without this, the publisher and consumer spans appear as **completely separate, unrelated traces**.

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
public class OrderCreatedConsumer(ISender sender) : IConsumer<OrderCreatedIntegrationEvent>
{
    public async Task Consume(IConsumeContext<OrderCreatedIntegrationEvent> context)
    {
        await sender.Send(
            new ProcessOrderCommand(context.Message.OrderId),
            context.CancellationToken);
    }
}
```

### Pattern 2: Direct Database Operations

```csharp
[ServiceBusQueue]
public class OrderStatusConsumer(ApplicationDbContext dbContext) : IConsumer<OrderStatusEvent>
{
    public async Task Consume(IConsumeContext<OrderStatusEvent> context)
    {
        Order? order = await dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == context.Message.OrderId, context.CancellationToken);

        if (order is null) return; // permanent — complete the message

        order.Status = context.Message.Status;
        await dbContext.SaveChangesAsync(context.CancellationToken);
        // DbContext disposed automatically after this returns
    }
}
```

### Pattern 3: Accessing Message Metadata

Use `context.Metadata` (`MessageContext`) for broker metadata. No Azure SDK import required in your consumer:

```csharp
[ServiceBusQueue]
public class IdempotentConsumer(ApplicationDbContext dbContext) : IConsumer<MyEvent>
{
    public async Task Consume(IConsumeContext<MyEvent> context)
    {
        // Idempotency check using broker-assigned MessageId
        string messageId = context.Metadata.MessageId;
        bool alreadyProcessed = await dbContext.ProcessedEvents
            .AnyAsync(e => e.MessageId == messageId, context.CancellationToken);

        if (alreadyProcessed) return;

        // Custom routing / conditional logic using application properties
        if (context.Metadata.ApplicationProperties.TryGetValue("EventType", out object? eventType))
        {
            // use for routing decisions...
        }

        await ProcessEvent(context.Message, context.CancellationToken);
        dbContext.ProcessedEvents.Add(new ProcessedEvent { MessageId = messageId });
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
```

Available `MessageContext` properties: `MessageId`, `SessionId`, `CorrelationId`, `DeliveryCount`, `SequenceNumber`, `EnqueuedTime`, `ContentType`, `Subject`, `ApplicationProperties`.

---

## 🚨 Error Handling Strategies

### Throw vs Don't Throw

```
✅ SUCCESS — no exception thrown
  → Message completed and removed from queue

⚠️ TRANSIENT ERROR — throw exception
  → Message abandoned, returns to queue for retry
  → Dead-lettered after MaxDeliveryCount attempts

🛑 PERMANENT ERROR — catch and don't re-throw
  → Message completed and removed (no retry)
```

### Strategy 1: Transient vs Permanent

```csharp
[ServiceBusQueue]
public class PaymentConsumer(ILogger<PaymentConsumer> logger) : IConsumer<PaymentEvent>
{
    public async Task Consume(IConsumeContext<PaymentEvent> context)
    {
        try
        {
            await ProcessPayment(context.Message, context.CancellationToken);
        }
        catch (HttpRequestException)
        {
            throw; // transient — network issue, allow retry
        }
        catch (ValidationException ex)
        {
            logger.LogError(ex, "Invalid payment data for event {EventId}", context.Message.Id);
            // permanent — don't throw, complete the message
        }
    }
}
```

### Strategy 2: Limiting Delivery Attempts with `MaxDeliveryCount` ✅ RECOMMENDED

Use the `MaxDeliveryCount` attribute property to dead-letter a message after N failed attempts, **without any code inside the consumer**:

```csharp
// Dead-letter after 3 failed attempts — zero extra code in the consumer
[ServiceBusQueue(MaxDeliveryCount = 3)]
public class PaymentConsumer : IConsumer<PaymentEvent>
{
    public async Task Consume(IConsumeContext<PaymentEvent> context)
    {
        // If this throws, the framework checks DeliveryCount automatically:
        // Attempt 1 → abandon | Attempt 2 → abandon | Attempt 3 → dead-letter
        await ProcessPayment(context.Message, context.CancellationToken);
    }
}
```

### Strategy 3: Delivery Count Guard (Business Logic)

Use `context.Metadata.DeliveryCount` when you need **business-logic decisions** based on how many times a message has been attempted:

```csharp
[ServiceBusQueue]
public class SafeConsumer(ILogger<SafeConsumer> logger) : IConsumer<MyEvent>
{
    public async Task Consume(IConsumeContext<MyEvent> context)
    {
        if (context.Metadata.DeliveryCount > 5)
        {
            logger.LogError(
                "Message {MessageId} exceeded max retries. Completing to prevent infinite loop.",
                context.Metadata.MessageId);
            return; // complete without retry
        }

        await DoWork(context.Message, context.CancellationToken);
    }
}
```

### Strategy 4: Idempotency Check

```csharp
[ServiceBusQueue]
public class IdempotentOrderConsumer(ApplicationDbContext dbContext) : IConsumer<OrderCreatedEvent>
{
    public async Task Consume(IConsumeContext<OrderCreatedEvent> context)
    {
        bool alreadyProcessed = await dbContext.ProcessedEvents
            .AnyAsync(e => e.EventId == context.Message.Id, context.CancellationToken);

        if (alreadyProcessed) return;

        await ProcessOrder(context.Message, context.CancellationToken);
        dbContext.ProcessedEvents.Add(new ProcessedEvent { EventId = context.Message.Id });
        await dbContext.SaveChangesAsync(context.CancellationToken);
    }
}
```

---

## 🎯 Best Practices

### ✅ DO

- **Put integration events in a dedicated `*.IntegrationEvents` classlib** — other services can reference only the shared contracts without depending on your consumers or application logic
- **Annotate every integration event** with `[QueueRoute("...")]` or `[TopicRoute("...")]` — single source of truth
- **Use `[ServiceBusQueue]` without a name** on consumers — the name is resolved from `[QueueRoute]` on the message type
- **Use `ServiceBusRoute.GetQueueName<TEvent>()`** in tests and provisioning tools — stays in sync with the attribute
- **Inject `IEventBusPublisher`** — never `ServiceBusPublisher` or `ServiceBusClient` directly
- **Reference only `BusWorks.Abstractions`** from application and domain projects — consumers then have zero Azure SDK dependency
- **Make operations idempotent** — messages can be delivered more than once
- **Distinguish transient vs permanent errors** — throw for retry, return for complete
- **Use `MaxDeliveryCount` on the attribute** to limit delivery attempts — avoids boilerplate in `Consume`
- **Use `context.Metadata.DeliveryCount`** only for business-logic decisions (e.g. fallback behaviour on later attempts), not for limiting retries
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
- **Don't reference `Azure.Messaging.ServiceBus`** from consumer or application projects — use `BusWorks.Abstractions` only
- **Don't log infrastructure details** — already captured in OTel spans
- **Don't forget idempotency** — duplicate delivery is normal
- **Don't make consumers `internal`** — consumer discovery requires `public` classes
- **Don't block threads** — use `async`/`await` throughout
- **Don't use `event.Id` (a unique Guid) as `SessionId`** — every message would be its own session, adding overhead with no ordering benefit
- **Don't set `MaxConcurrentCallsPerSession` > 1** — this breaks the FIFO ordering guarantee within a session
- **Don't send a message without `SessionId` to a session-enabled queue** — the broker will reject it; the startup contract validation catches this at application start

### 🏆 Checklist

- [ ] Integration events are in a dedicated `*.IntegrationEvents` classlib referencing only `BusWorks.Abstractions`
- [ ] Integration event inherits `IntegrationEvent` from `BusWorks.Abstractions`
- [ ] Integration event is annotated with `[QueueRoute("...")]` or `[TopicRoute("...")]`
- [ ] Consumer class is `public` with `[ServiceBusQueue]` or `[ServiceBusTopic("subscription-name")]`
- [ ] Consumer implements `IConsumer<TMessage>` (from `BusWorks.Abstractions`)
- [ ] Consumer project references only `BusWorks.Abstractions` — not the full `BusWorks` package
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
- Does it implement `IConsumer<TMessage>`?
- Is the consumer's assembly passed to `AddEventBus(..., typeof(MyConsumer).Assembly)`?
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

Fix: make the consumer attribute match what the event declares.

### `ServiceBus:Setup:Error` span at startup

- Does the queue/topic exist in Azure Service Bus / emulator?
- Is the `EventBusOptions` section correct in `appsettings.json`?

### Publish span has no child consumer span (broken distributed trace)

- Verify `ServiceBusPublisher` is injecting `traceparent` into `ApplicationProperties` (it does by default)
- Confirm both publisher and consumer are reporting to the same OTel collector

### Messages being retried infinitely

- Don't re-throw for permanent errors (validation failures, not-found records, etc.)
- Set `MaxDeliveryCount` on the `[ServiceBusQueue]` or `[ServiceBusTopic]` attribute
- Ensure Azure entities are provisioned with a high `MaxDeliveryCount` (e.g. `100`)
- Or add a `context.Metadata.DeliveryCount > N` guard inside `Consume` for business-logic-driven decisions

### `InvalidOperationException` at startup — session contract mismatch

```
// RequireSession = true but message does not implement ISessionedEvent:
Consumer 'PaymentCommandConsumer' has RequireSession = true, but message type
'PaymentCommand' does not implement ISessionedEvent.

// Message implements ISessionedEvent but consumer does not have RequireSession = true:
Message type 'PaymentCommand' implements ISessionedEvent (declares a SessionId),
but consumer 'PaymentCommandConsumer' does not have RequireSession = true.
```

Fix: ensure both sides agree — `ISessionedEvent` on the event **and** `RequireSession = true` on the consumer, or neither.

### Session messages not received / consumer appears idle

- Verify the Azure entity has `requiresSession: true`
- Verify the event's `SessionId` property returns a non-null, non-empty string
- Check `MaxConcurrentSessions` — if all slots are held by long-running sessions, new sessions queue up

### Session messages processing out of order

- Verify `MaxConcurrentCallsPerSession` is `1` (the default)
- Check that no separate non-session processor is also attached to the same queue

---

## 📚 Additional Resources

- **Example consumers:** `BusWorks/Consumer/ExampleServiceBusConsumer.cs`
- **Consumer interfaces:** `BusWorks.Abstractions/Consumer/IConsumer.cs`
- **Message metadata:** `BusWorks.Abstractions/Consumer/MessageContext.cs`
- **Consumer background service:** `BusWorks/BackgroundServices/ServiceBusProcessorBackgroundService.cs`
- **Publisher implementation:** `BusWorks/Publisher/ServiceBusPublisher.cs`
- **Publisher interface:** `BusWorks.Abstractions/IEventBusPublisher.cs`
- **Route attributes:** `BusWorks.Abstractions/Attributes/ServiceBusRouteAttributes.cs`
- **Route helper:** `BusWorks.Abstractions/ServiceBusRoute.cs`
- **Session interface:** `BusWorks.Abstractions/ISessionedEvent.cs`
- **Azure Service Bus Docs:** [Microsoft Learn](https://learn.microsoft.com/azure/service-bus-messaging/)
- **Azure Service Bus Sessions:** [Session documentation](https://learn.microsoft.com/azure/service-bus-messaging/message-sessions)
- **OpenTelemetry Messaging Conventions:** [Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/messaging/)

---

**Last Updated:** March 31, 2026  
**Key files:** `IConsumer.cs`, `MessageContext.cs`, `IEventBusPublisher.cs`, `ServiceBusRouteAttributes.cs`, `ServiceBusRoute.cs`, `IntegrationEvent.cs`, `ISessionedEvent.cs`, `ServiceBusProcessorBackgroundService.cs`






