# Commands vs Events

OpinionatedEventing makes a strict distinction between commands and events. Understanding the difference is the first step to designing a well-structured message-driven system.

## The core difference

| | Command | Event |
|---|---|---|
| **Marker interface** | `ICommand` | `IEvent` |
| **Intent** | "Please do X" | "X happened" |
| **Handlers** | Exactly one | Zero or more |
| **Broker topology** | Point-to-point (queue) | Publish-subscribe (topic / exchange) |
| **Direction** | Sender → specific service | Sender → any interested party |

## Events

An event records something that already happened. It is a fact — immutable and past tense.

```csharp
public record OrderPlaced(Guid OrderId, decimal Total) : IEvent;
public record PaymentReceived(Guid OrderId, Guid PaymentId) : IEvent;
public record StockReserved(Guid OrderId, string Sku, int Quantity) : IEvent;
```

Events are broadcast to a topic (Azure Service Bus) or exchange (RabbitMQ). Every service that has registered an `IEventHandler<T>` for that event type receives a copy. If no handler exists, the message is silently discarded — events have no required audience.

```csharp
// Multiple handlers can react to the same event.
public class NotificationHandler : IEventHandler<OrderPlaced> { ... }
public class AuditHandler : IEventHandler<OrderPlaced> { ... }
public class OrderSaga : IEventHandler<OrderPlaced> { ... } // started by the saga engine
```

**Use an event when:**
- Something happened in your domain that other parts of the system may care about
- You want to decouple the producer from its consumers
- Multiple downstream systems need to react independently
- You cannot predict which services will be interested in the future

## Commands

A command is a request to perform a specific action. It has intent and a named target.

```csharp
public record ProcessPayment(Guid OrderId, decimal Amount) : ICommand;
public record ReserveStock(Guid OrderId, string Sku, int Quantity) : ICommand;
public record CancelPayment(Guid OrderId, string Reason) : ICommand;
```

Commands are routed to a single queue and consumed by exactly one handler. The framework enforces this at startup — registering two `ICommandHandler<ProcessPayment>` implementations throws an `InvalidOperationException`.

```csharp
// Only one handler allowed per command type.
public class PaymentCommandHandler : ICommandHandler<ProcessPayment> { ... }
```

**Use a command when:**
- You need a specific service to perform a specific action
- You need clear ownership — one service is responsible for executing the operation
- You are inside a saga and orchestrating a workflow

## Why the strict separation matters

### Topology maps to intent

Events use publish-subscribe topology: one producer, many consumers. Commands use point-to-point topology: one sender, one receiver. Using the wrong abstraction for the wrong intent leads to subtle bugs:

- Sending a "command" as an event means you cannot guarantee it is handled by exactly one consumer
- Publishing a "state change" as a command means only one service ever learns about it

The compiler enforces the distinction through `IEvent` and `ICommand` marker interfaces. The framework maps each to the correct broker topology automatically.

### Decoupling

Events give the producing service freedom — it does not need to know who cares. Consumers can be added or removed without touching the producer. Commands do the opposite: the sender explicitly names the recipient service via the queue name, which is intentional for directed operations.

### Causation chain

Both events and commands carry `CorrelationId` (the ID of the originating request) and `CausationId` (the ID of the message that caused this one to be sent). This lets you reconstruct the full chain of events and commands across services in traces and logs.

## Naming conventions

Use **past-tense verbs** for events, **imperative verbs** for commands:

| Good event names | Good command names |
|---|---|
| `OrderPlaced` | `ProcessPayment` |
| `PaymentReceived` | `ReserveStock` |
| `StockReserved` | `SendConfirmationEmail` |
| `OrderShipped` | `CancelOrder` |

Avoid generic names like `OrderUpdated` or `DoSomething`. The name should communicate precisely what happened or what is being requested.

## Naming the broker resources

By convention the framework derives broker resource names from the message type name:

| Type | Derived name |
|---|---|
| `OrderPlaced` (event) | topic/exchange: `order-placed` |
| `ProcessPayment` (command) | queue: `process-payment` |

Override the default with `[MessageTopic]` or `[MessageQueue]` attributes:

```csharp
[MessageTopic("payments.events.order-placed")]
public record OrderPlaced(Guid OrderId, decimal Total) : IEvent;

[MessageQueue("payments.commands.process-payment")]
public record ProcessPayment(Guid OrderId, decimal Amount) : ICommand;
```

## Publishing

Both events and commands are published through `IPublisher`. The interface is the same; the routing is different.

```csharp
// Fan-out to all IEventHandler<OrderPlaced> registrations.
await publisher.PublishEventAsync(new OrderPlaced(order.Id, total), ct);

// Point-to-point to the single ICommandHandler<ProcessPayment>.
await publisher.SendCommandAsync(new ProcessPayment(order.Id, total), ct);
```

Both calls write to the outbox within the caller's transaction — the actual broker delivery happens asynchronously. See [Outbox Pattern](outbox-pattern.md) for details.
