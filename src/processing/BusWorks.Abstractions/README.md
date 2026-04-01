# BusWorks.Abstractions

Framework contracts for [BusWorks](https://www.nuget.org/packages/BusWorks) — the Azure Service Bus message processor.

This package has **zero NuGet dependencies** by design. Reference it from your Application and Domain layers to define integration events and consumers without taking any Azure SDK dependency.

## 📦 What's Included

| Type | Description |
|---|---|
| `IIntegrationEvent` | Marker interface for all integration events |
| `IntegrationEvent` | Base record with `Id` and `OccurredOnUtc` |
| `ISessionedEvent` | Implement on events that require FIFO ordering per `SessionId` |
| `IEventBusPublisher` | Publishing contract — inject this, never the concrete implementation |
| `IConsumer<T>` | Consumer interface — implement to handle a message type |
| `IConsumeContext<T>` | Exposes the deserialized message and broker-agnostic metadata |
| `MessageContext` | Broker-agnostic metadata: `MessageId`, `SessionId`, `DeliveryCount`, etc. |
| `ServiceBusRoute` | Helper to resolve queue/topic names from route attributes at runtime |
| `[QueueRoute]` | Declare a queue name once on an integration event record |
| `[TopicRoute]` | Declare a topic name once on an integration event record |
| `[ServiceBusQueue]` | Decorate a consumer class to bind it to a queue |
| `[ServiceBusTopic]` | Decorate a consumer class to bind it to a topic subscription |

## 🚀 Quick Start

### 1. Define an Integration Event

```csharp
using BusWorks.Abstractions;
using BusWorks.Attributes;

[QueueRoute("user-created-events")]
public sealed record UserCreatedIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string Email,
    string Name)
    : IntegrationEvent(Id, OccurredOnUtc);
```

### 2. Implement a Consumer

```csharp
using BusWorks.Attributes;
using BusWorks.Consumer;

[ServiceBusQueue]
public class UserCreatedConsumer(IEmailService emailService) : IConsumer<UserCreatedIntegrationEvent>
{
    public async Task Consume(IConsumeContext<UserCreatedIntegrationEvent> context)
    {
        await emailService.SendWelcomeEmailAsync(context.Message.Email, context.CancellationToken);
    }
}
```

### 3. Publish an Event

```csharp
public class RegisterUserCommandHandler(IEventBusPublisher eventBus)
{
    public async Task Handle(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        await eventBus.PublishAsync(
            new UserCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, command.Email, command.Name),
            cancellationToken);
    }
}
```

## 🏗️ Recommended Project Layout

```
MyService.IntegrationEvents/     ← references BusWorks.Abstractions only
    UserCreatedIntegrationEvent.cs

MyService.Application/           ← references BusWorks.Abstractions
    Consumers/
        UserCreatedConsumer.cs

MyService.Infrastructure/        ← references BusWorks (full package)
    Program.cs
```

The Application and Domain layers never depend on the Azure SDK — only on this package.

## 🔗 Related Packages

- **[BusWorks](https://www.nuget.org/packages/BusWorks)** — Infrastructure implementation (Azure Service Bus, DI wiring, background processor)

