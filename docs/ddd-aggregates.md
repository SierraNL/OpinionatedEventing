# DDD Aggregates

OpinionatedEventing provides first-class support for the Domain-Driven Design (DDD) pattern of aggregate roots that raise domain events. Domain events are written to the outbox automatically, within the same EF Core `SaveChanges` transaction as the aggregate itself.

## Defining an aggregate root

Extend `AggregateRoot` and call `RaiseDomainEvent` inside your domain methods:

```csharp
using OpinionatedEventing;

public class Order : AggregateRoot
{
    public Guid Id { get; private set; }
    public decimal Total { get; private set; }
    public OrderStatus Status { get; private set; }

    private Order() { } // EF Core needs a parameterless constructor

    public static Order Create(decimal total)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            Total = total,
            Status = OrderStatus.Pending
        };
        order.RaiseDomainEvent(new OrderPlaced(order.Id, total));
        return order;
    }

    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Shipped)
            throw new InvalidOperationException("Cannot cancel a shipped order.");

        Status = OrderStatus.Cancelled;
        RaiseDomainEvent(new OrderCancelled(Id, reason));
    }
}
```

## Rules for aggregates

**Do call `RaiseDomainEvent` when state changes.** Every meaningful state transition should produce an event that describes what happened.

**Do not inject `IPublisher` into aggregates.** Aggregates must not know about infrastructure. `RaiseDomainEvent` queues the event in memory — the `DomainEventInterceptor` writes it to the outbox.

**Do not call `SaveChanges` inside aggregate methods.** Persistence is the application layer's concern.

**Do keep aggregates pure.** Aggregate methods should change state and raise events — nothing else. No database calls, no HTTP calls, no `async`.

## How domain events reach the outbox

When you call `db.SaveChangesAsync()`, the `DomainEventInterceptor` fires before EF writes to the database:

1. It scans `DbContext.ChangeTracker` for tracked entities that implement `IAggregateRoot`
2. For each aggregate with pending domain events, it creates an `OutboxMessage` per event
3. It adds those `OutboxMessage` rows to the change tracker
4. It calls `aggregate.ClearDomainEvents()` so events are not re-harvested on the next save
5. EF writes both the aggregate rows and the outbox rows in a single transaction

The entire process is invisible to application code. You write:

```csharp
var order = Order.Create(total);
db.Orders.Add(order);
await db.SaveChangesAsync(ct);
// ↑ Order row + OutboxMessage(OrderPlaced) committed atomically
```

## Wiring up the interceptor

Register the interceptor when configuring your `DbContext`:

```csharp
services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlServer(connectionString);
    options.AddInterceptors(sp.GetRequiredService<DomainEventInterceptor>());
});
services.AddOpinionatedEventingEntityFramework<AppDbContext>();
```

The `DomainEventInterceptor` is registered as scoped by `AddOpinionatedEventingEntityFramework`.

## Correlation propagation

Every domain event written to the outbox carries:

- `CorrelationId` — taken from `IMessagingContext.CorrelationId` in the current scope, which is the correlation ID of the inbound message being processed (or a new root ID if initiated from an HTTP request)
- `CausationId` — taken from `IMessagingContext.CausationId`, linking this event to the message that triggered the current handler

This means the full causal chain is captured in the outbox rows and propagated through the broker to consumers.

## AggregateRoot API

```csharp
public abstract class AggregateRoot : IAggregateRoot
{
    // Queued domain events, cleared after each SaveChanges.
    public IReadOnlyList<IEvent> DomainEvents { get; }

    // Call inside domain methods to queue a domain event.
    protected void RaiseDomainEvent(IEvent domainEvent);
}
```

`ClearDomainEvents()` is internal — only the framework calls it. Application code should never clear the events list.

## Using IAggregateRoot without the base class

If your aggregate already extends another base class, implement `IAggregateRoot` directly:

```csharp
public interface IAggregateRoot
{
    IReadOnlyList<IEvent> DomainEvents { get; }
    internal void ClearDomainEvents();
}
```

Because `ClearDomainEvents` is `internal`, you will need to provide your own implementation of the domain events list. Consider using the `AggregateRoot` base class whenever possible.

## Multiple aggregates in one transaction

The interceptor scans **all** tracked aggregates. If a single `SaveChanges` modifies multiple aggregates, all their domain events are harvested and written to the outbox atomically:

```csharp
db.Orders.Add(order);   // raises OrderPlaced
db.Invoices.Add(invoice); // raises InvoiceCreated
await db.SaveChangesAsync(ct);
// Both OutboxMessage rows committed with both entity rows.
```

## Testing aggregates

Test aggregate logic without a database by inspecting `DomainEvents` directly:

```csharp
var order = Order.Create(100m);

Assert.Single(order.DomainEvents);
var evt = Assert.IsType<OrderPlaced>(order.DomainEvents[0]);
Assert.Equal(100m, evt.Total);
```

For end-to-end tests that include outbox writes, use `InMemoryOutboxStore`:

```csharp
services.AddSingleton<IOutboxStore, InMemoryOutboxStore>();

// After SaveChanges:
var store = sp.GetRequiredService<InMemoryOutboxStore>();
Assert.Single(store.PendingMessages);
```
