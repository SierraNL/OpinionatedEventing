# OpinionatedEventing.Abstractions

Pure contracts for the OpinionatedEventing library suite — marker interfaces, handler contracts, and base types. No NuGet dependencies.

Reference this package from domain and application assemblies that only need messaging contracts. Composition-root and infrastructure assemblies should reference `OpinionatedEventing` instead.

## Installation

```
dotnet add package OpinionatedEventing.Abstractions
```

## What's included

| Type | Purpose |
|---|---|
| `IEvent` | Marker interface for fan-out domain/integration events |
| `ICommand` | Marker interface for point-to-point commands |
| `IEventHandler<TEvent>` | Handle an event (multiple registrations allowed per type) |
| `ICommandHandler<TCommand>` | Handle a command (exactly one registration per type) |
| `IPublisher` | Write events and commands to the outbox |
| `IMessagingContext` | Ambient correlation and causation identifiers for the current handler scope |
| `IMessageHandlerRunner` | Dispatches inbound messages to their registered handler(s) |
| `IAggregateRoot` | Marks a class as a DDD aggregate root that collects domain events |
| `AggregateRoot` | Convenience base class implementing `IAggregateRoot` |
| `IConsumerPauseController` | Controls whether broker consumer workers should pause |
| `IOutboxStore` | Persistence contract for the outbox |
| `OutboxMessage` | Represents a message stored in the outbox pending delivery |
| `MessageTopicAttribute` | Overrides the default broker topic name for an `IEvent` |
| `MessageQueueAttribute` | Overrides the default broker queue name for an `ICommand` |

## Quick start

Define your messages in a contracts assembly:

```csharp
// Events fan out to all registered handlers
public record OrderPlaced(Guid OrderId, decimal Total) : IEvent;

// Commands go to exactly one handler
public record ShipOrder(Guid OrderId) : ICommand;
```

Implement handlers in an application assembly:

```csharp
public class OrderPlacedHandler : IEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced @event, CancellationToken ct)
    {
        // ...
        return Task.CompletedTask;
    }
}
```

Wire up DI and the runtime in your composition root using `OpinionatedEventing`:

```csharp
builder.Services
    .AddOpinionatedEventing()
    .AddHandlersFromAssemblies(Assembly.GetExecutingAssembly());
```

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)
