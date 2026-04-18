# OpinionatedEventing.Core

Core abstractions for the OpinionatedEventing library suite. This package contains the marker interfaces, handler contracts, and base types that every other OpinionatedEventing package builds on. It has no infrastructure dependencies.

## Installation

```
dotnet add package OpinionatedEventing.Core
```

## What's included

| Type | Purpose |
|---|---|
| `IEvent` | Marker interface for fan-out events (topic/exchange) |
| `ICommand` | Marker interface for point-to-point commands (queue) |
| `IEventHandler<TEvent>` | Handle an event |
| `ICommandHandler<TCommand>` | Handle a command |
| `IPublisher` | Write events and commands to the outbox |
| `IOutboxStore` | Abstraction for the outbox persistence layer |
| `AggregateRoot` | Base class for DDD aggregates with domain event harvesting |

## Quick start

Define your messages:

```csharp
// Events fan out to all registered handlers
public record OrderPlaced(Guid OrderId, decimal Total) : IEvent;

// Commands go to exactly one handler
public record ShipOrder(Guid OrderId) : ICommand;
```

Implement handlers:

```csharp
public class OrderPlacedHandler : IEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced @event, CancellationToken ct = default)
    {
        // ...
        return Task.CompletedTask;
    }
}
```

Register with DI:

```csharp
builder.Services
    .AddOpinionatedEventing()
    .AddOutbox()          // OpinionatedEventing.Outbox
    .AddRabbitMQTransport(...);  // or .AddAzureServiceBusTransport(...)
```

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)
