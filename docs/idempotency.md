# Idempotency and the Inbox Pattern

The outbox pattern guarantees **at-least-once delivery**: every message written to the outbox will eventually reach the broker, but under certain failure conditions (process crash between dispatch and acknowledgement) a message may be delivered more than once. Consumers must therefore be prepared to receive duplicates.

OpinionatedEventing does **not** provide an inbox pattern implementation for V1.0. This is an explicit design decision: adding a shared inbox store would introduce a mandatory persistence dependency into every consuming service, even when the handler is naturally idempotent or the business risk of a duplicate is low. The decision is re-evaluated after gathering real-world usage data.

## Why duplicates happen

The `OutboxDispatcherWorker` marks a message as processed only after the broker confirms receipt. If the process crashes or restarts after the broker receives the message but before the confirmation is written to the database, the worker will re-dispatch the same row on the next poll cycle. The broker receives the message twice, and both deliveries reach your handlers.

## Strategies for consumer-side idempotency

### 1. Natural idempotency

The simplest case: the handler's effect is idempotent by construction. A handler that sets a flag, updates a value to a fixed state, or performs a read-only side effect can safely run twice without harm. Prefer this approach where the domain allows it.

```csharp
public class MarkOrderPaidHandler : IEventHandler<OrderPaid>
{
    public async Task HandleAsync(OrderPaid @event, CancellationToken ct)
    {
        // UPDATE ... SET Status = 'Paid' WHERE Id = @id
        // Running this twice is harmless — the row ends up in the same state.
        await _db.Orders
            .Where(o => o.Id == @event.OrderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Paid), ct);
    }
}
```

### 2. Idempotency key check (manual inbox)

Track processed message IDs in a database table. Before executing the handler's business logic, check whether the message ID has already been recorded. If it has, skip the work. Record the ID and commit in the same transaction as the business operation.

`IMessagingContext.MessageId` carries the inbound message's own ID — inject `IMessagingContext` into your handler and use it as the deduplication key:

```csharp
public class SendWelcomeEmailHandler(IMessagingContext messagingContext, AppDbContext db, IEmailClient emailClient)
    : IEventHandler<UserRegistered>
{
    public async Task HandleAsync(UserRegistered @event, CancellationToken ct)
    {
        bool alreadyProcessed = await db.ProcessedMessages
            .AnyAsync(m => m.MessageId == messagingContext.MessageId, ct);

        if (alreadyProcessed)
            return;

        await emailClient.SendWelcomeEmailAsync(@event.Email, ct);

        db.ProcessedMessages.Add(new ProcessedMessage(messagingContext.MessageId));
        await db.SaveChangesAsync(ct);
    }
}
```

Prune the `ProcessedMessages` table periodically — rows older than your message retention window (e.g. 7 days) are safe to delete.

### 3. Conditional insert / unique constraint

Use a database unique constraint on the message ID column instead of an explicit `SELECT` before `INSERT`. This avoids the read entirely and lets the database enforce uniqueness. Use `IMessagingContext.MessageId` as the deduplication key (same as strategy 2).

```sql
CREATE UNIQUE INDEX ux_processed_messages_id ON processed_messages (message_id);
```

Catch the resulting `DbUpdateException` / unique-constraint violation and treat it as "already processed". The exact inner-exception type is provider-specific (SQL Server throws `SqlException`, PostgreSQL throws `NpgsqlException`), so check the message text or use a provider helper:

```csharp
try
{
    _db.ProcessedMessages.Add(new ProcessedMessage(@event.MessageId));
    await _db.SaveChangesAsync(ct);
}
catch (DbUpdateException ex) when (
    ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true)
{
    return; // already processed — duplicate delivery
}

// execute business logic here — only reached on first delivery
await DoWorkAsync(@event, ct);
```

### 4. Idempotent external calls

When the side effect is a call to an external API, pass the message ID as the idempotency key if the API supports it (most payment, email, and notification APIs do). Inject `IMessagingContext` into your handler to access `MessageId`:

```csharp
await _paymentGateway.ChargeAsync(new ChargeRequest
{
    IdempotencyKey = _messagingContext.MessageId.ToString(),
    Amount = @event.Amount,
    Currency = @event.Currency,
});
```

The API will return the original response for any duplicate call with the same key, and your handler exits cleanly.

## Which strategy to use

| Scenario | Recommended strategy |
|---|---|
| Effect is a state-machine transition or idempotent update | Natural idempotency |
| Effect must run exactly once, DB available in handler | Unique-constraint insert |
| External API with idempotency-key support | Pass message ID as key |
| Multiple side effects that must all-or-nothing deduplicate | Manual inbox with transaction |

## Future versions

An `IInboxStore` abstraction (modelled after `IOutboxStore`) with an EF Core implementation is a candidate for a future release. If you need it now, the patterns above cover the same ground with code you control.
