# OpinionatedEventing.Outbox

Outbox dispatcher background service for OpinionatedEventing. All outbound messages are written to `IOutboxStore` within the caller's `SaveChanges` transaction, then delivered to the broker asynchronously by `OutboxDispatcherWorker`. This guarantees at-least-once delivery without dual-write risk.

## Installation

```
dotnet add package OpinionatedEventing.Outbox
```

Pair with an outbox store implementation:

```
dotnet add package OpinionatedEventing.EntityFramework
```

## Registration

```csharp
builder.Services
    .AddOpinionatedEventing()
    .AddOutbox();
```

`AddOutbox` registers:
- `IPublisher` → `OutboxPublisher` (scoped) — writes to the outbox store
- `OutboxDispatcherWorker` — hosted service that polls and dispatches

## How it works

1. Your code calls `IPublisher.PublishEventAsync` or `SendCommandAsync` inside a unit of work.
2. `OutboxPublisher` writes `OutboxMessage` records to `IOutboxStore` **within the same transaction**.
3. `OutboxDispatcherWorker` picks up pending messages and forwards them to the configured transport.

```csharp
public class PlaceOrderHandler : ICommandHandler<PlaceOrder>
{
    private readonly AppDbContext _db;
    private readonly IPublisher _publisher;

    public async Task HandleAsync(PlaceOrder command, CancellationToken ct = default)
    {
        var order = new Order(command.OrderId);
        _db.Orders.Add(order);
        await _publisher.PublishEventAsync(new OrderPlaced(order.Id), ct);
        await _db.SaveChangesAsync(ct); // outbox write is part of this transaction
    }
}
```

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)
