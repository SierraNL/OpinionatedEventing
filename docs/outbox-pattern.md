# Outbox Pattern

The outbox pattern guarantees that every message your application writes is eventually delivered to the broker — even if the broker is temporarily unavailable when your code runs.

## The problem it solves

Without the outbox, a typical event-publishing flow looks like this:

```
1. Insert Order row into database
2. Publish OrderPlaced event to broker
3. Commit database transaction
```

This has a critical flaw: steps 1–3 are not atomic. If the broker call in step 2 fails, the Order is saved but no event is published. If the process crashes after step 2 but before step 3, the event is published but the Order is not saved. Either way your data and your messages are out of sync.

## How the outbox solves it

The outbox replaces the direct broker call with a write to an `outbox_messages` table in the same database transaction as your business data:

```
1. Insert Order row into database
2. Insert OutboxMessage row into outbox_messages
3. Commit database transaction  ← both rows commit or neither does
```

A background `OutboxDispatcherWorker` then reads the pending rows and forwards them to the broker:

```
4. Worker reads pending OutboxMessage rows
5. Calls ITransport.SendAsync() for each message
6. On success: marks message ProcessedAt (delivered)
7. On failure: increments AttemptCount, retries up to MaxAttempts
8. After MaxAttempts: marks message FailedAt (dead-letter)
```

Steps 4–8 can be retried indefinitely. The broker call is idempotent from the outbox's perspective — if the process crashes between step 5 and 6, the worker simply picks up the same row again on the next poll.

## There is no direct publish path

`IPublisher` writes **only** to the outbox. There is intentionally no method to publish directly to the broker. This design choice means:

- You never accidentally bypass the transactional guarantee
- The outbox is always the source of truth for what needs to be sent
- The broker being slow, unavailable, or restarted does not affect your write path

## Atomic writes with EF Core

When you use `OpinionatedEventing.EntityFramework`, the outbox write is fully automatic for domain events raised on aggregate roots. The `DomainEventInterceptor` hooks `DbContext.SaveChangesAsync()` and harvests any `IEvent` instances queued on your aggregates:

```csharp
// Inside your aggregate:
public void Place(decimal total)
{
    Total = total;
    Status = OrderStatus.Placed;
    RaiseDomainEvent(new OrderPlaced(Id, total)); // queued, not written yet
}

// In your application code:
db.Orders.Add(order);
order.Place(total);
await db.SaveChangesAsync(ct);
// ↑ DomainEventInterceptor fires here, harvests OrderPlaced,
//   creates OutboxMessage, writes both rows in one transaction.
```

If you need to publish an event from application code (not from an aggregate), inject `IPublisher`:

```csharp
await publisher.PublishEventAsync(new OrderPlaced(orderId, total), ct);
await db.SaveChangesAsync(ct); // commits both the business row and the outbox row
```

## OutboxMessage structure

Each row in `outbox_messages` contains:

| Column | Purpose |
|---|---|
| `Id` | Unique message identifier (Guid) |
| `MessageType` | Assembly-qualified CLR type name for deserialization |
| `Payload` | JSON-serialized message body |
| `MessageKind` | `"Event"` or `"Command"` |
| `CorrelationId` | Chain identifier propagated from the originating request |
| `CausationId` | ID of the message that caused this one to be published |
| `CreatedAt` | When the row was written |
| `ProcessedAt` | Set when successfully dispatched to the broker |
| `FailedAt` | Set when MaxAttempts is exhausted (dead-lettered) |
| `AttemptCount` | Number of dispatch attempts made |
| `Error` | Last dispatch error message |

## Retry behaviour

The `OutboxDispatcherWorker` retries failed dispatches up to `OutboxOptions.MaxAttempts` (default: 5). Between attempts the worker continues processing other messages — there is no exponential back-off by default, but the `PollInterval` (default: 1 second) acts as a natural delay.

After `MaxAttempts` failures the row is marked as dead-lettered (`FailedAt` is set). Dead-lettered rows are never retried automatically. Use `IOutboxMonitor.GetDeadLetterCountAsync()` to detect accumulation and alert on it.

## Configuring the dispatcher

```csharp
services.AddOpinionatedEventing(options =>
{
    options.Outbox.PollInterval = TimeSpan.FromSeconds(1);   // how often to poll
    options.Outbox.BatchSize = 50;                           // messages per poll cycle
    options.Outbox.MaxAttempts = 5;                          // before dead-lettering
    options.Outbox.ConcurrentWorkers = 1;                    // parallel dispatch workers
});
```

Increase `ConcurrentWorkers` to raise throughput. Each worker polls and dispatches independently. The `IOutboxStore` implementation is responsible for ensuring that concurrent workers do not pick up the same message (the EF Core store handles this with row-level locking).

## IOutboxStore

`IOutboxStore` is an abstraction — implementations live in separate packages. The framework ships two:

| Implementation | Package | Purpose |
|---|---|---|
| `EFCoreOutboxStore<TDbContext>` | `OpinionatedEventing.EntityFramework` | Production use |
| `InMemoryOutboxStore` | `OpinionatedEventing.Testing` | Unit and integration tests |

You can implement `IOutboxStore` yourself to support other persistence backends.

## Health monitoring

Use `IOutboxMonitor` to observe the backlog:

```csharp
public class OutboxHealthEndpoint(IOutboxMonitor monitor)
{
    public async Task<int> GetPendingCountAsync(CancellationToken ct)
        => await monitor.GetPendingCountAsync(ct);

    public async Task<int> GetDeadLetterCountAsync(CancellationToken ct)
        => await monitor.GetDeadLetterCountAsync(ct);
}
```

Or use the built-in health check extension (from `OpinionatedEventing.Aspire`):

```csharp
services.AddHealthChecks()
    .AddOpinionatedEventingHealthChecks(options =>
    {
        options.OutboxBacklogThreshold = 100; // Degraded above this
    });
```

## Delivery guarantees and consumer idempotency

The outbox guarantees **at-least-once delivery** — under certain failure conditions the same message may be delivered more than once. Consumers are responsible for handling duplicates. See [Idempotency](idempotency.md) for strategies and code examples.

## EF Core migration helpers

Rather than writing migration SQL by hand, use the provided extension methods:

```csharp
// In your migration's Up() method:
migrationBuilder.CreateOutboxTable();
migrationBuilder.CreateSagaStateTable(); // if using sagas

// In your migration's Down() method:
migrationBuilder.DropOutboxTable();
migrationBuilder.DropSagaStateTable();
```
