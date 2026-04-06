# BusWorks

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![Azure Service Bus](https://img.shields.io/badge/Azure-Service%20Bus-0078D4?logo=microsoftazure)
![License](https://img.shields.io/badge/license-MIT-green)

A .NET 10 framework for publishing and consuming **Azure Service Bus** messages, following Clean Architecture principles — plus a self-hostable browser-based management UI.

---

## Repository layout

```
src/
  processing/
    BusWorks.Abstractions/   ← NuGet: framework contracts, zero Azure SDK dependency
    BusWorks/                ← NuGet: infrastructure implementation (Azure SDK, DI, OTel)
  viewers/
    BusWorks.Viewer/         ← Blazor Server management UI (queues, topics, subscriptions)
    BusWorks.Viewer.Console/ ← Console-based viewer companion
  examples/
    BusWorks.Examples.*/     ← Runnable sender / receiver examples
  aspire/
    BusWorks.AppHost/        ← .NET Aspire orchestration
tests/
  BusWorks.Tests/            ← Integration + unit tests
```

---

## Packages

### [`BusWorks.Abstractions`](src/processing/BusWorks.Abstractions)

Framework contracts for your Application and Domain layers. **Zero NuGet dependencies by design.**

```xml
<PackageReference Include="BusWorks.Abstractions" Version="*" />
```

Reference this from your consumer and integration-events projects. Your application code never takes an Azure SDK dependency.  
→ [Full documentation](src/processing/BusWorks.Abstractions/README.md)

### [`BusWorks`](src/processing/BusWorks)

Infrastructure implementation — Azure Service Bus client, background processor, publisher, DI wiring, and OpenTelemetry tracing.

```xml
<PackageReference Include="BusWorks" Version="*" />
```

Reference this only from your Infrastructure / startup project.  
→ [Full documentation](src/processing/BusWorks/README.md)

---

## Quick start

### 1. Install packages

```xml
<!-- MyService.Application / consumer project -->
<PackageReference Include="BusWorks.Abstractions" Version="*" />

<!-- MyService.Infrastructure / startup project -->
<PackageReference Include="BusWorks" Version="*" />
```

### 2. Define an event

```csharp
[QueueRoute("orders")]
public sealed record OrderCreatedEvent(Guid Id, DateTime OccurredOnUtc, string CustomerId)
    : IntegrationEvent(Id, OccurredOnUtc);
```

### 3. Implement a consumer

```csharp
[ServiceBusQueue]
public class OrderCreatedConsumer(ISender sender) : IConsumer<OrderCreatedEvent>
{
    public Task Consume(IConsumeContext<OrderCreatedEvent> context) =>
        sender.Send(new ProcessOrderCommand(context.Message.CustomerId), context.CancellationToken);
}
```

### 4. Register at startup

```csharp
services.AddEventBus(configuration, typeof(OrderCreatedConsumer).Assembly);
```

### 5. Publish

```csharp
await eventBus.PublishAsync(new OrderCreatedEvent(Guid.NewGuid(), DateTime.UtcNow, customerId));
```

---

## BusWorks.Viewer

A self-hostable Blazor Server application for inspecting and managing your Service Bus namespace — create, edit, delete queues and topics, peek messages, send test messages, and manage subscriptions.

→ [Documentation & publish instructions](src/viewers/BusWorks.Viewer/README.md)

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).
