# Saga Orchestration

A saga manages a long-running business process that spans multiple services and messages. The orchestration style uses a central `SagaOrchestrator<TSagaState>` that explicitly coordinates each step — sending commands, reacting to events, handling timeouts, and compensating on failure.

## When to use orchestration

Use orchestration when:

- The workflow has a clear sequence of steps with explicit dependencies
- You need timeout and compensation logic (rollback on failure)
- You want a single class that makes the full flow readable
- The number of participating services is known upfront

For looser coupling where each service reacts to events independently, see [Saga Choreography](sagas-choreography.md).

## Defining a saga

Create a POCO state class and an orchestrator:

```csharp
// The state is serialized to JSON and persisted between steps.
public class OrderSagaState
{
    public Guid OrderId { get; set; }
    public Guid? PaymentId { get; set; }
    public bool PaymentConfirmed { get; set; }
    public bool StockReserved { get; set; }
}

public class OrderSaga : SagaOrchestrator<OrderSagaState>
{
    protected override void Configure(ISagaBuilder<OrderSagaState> builder)
    {
        builder
            .StartWith<OrderPlaced>(async (evt, state, ctx) =>
            {
                state.OrderId = evt.OrderId;
                await ctx.SendCommandAsync(new ProcessPayment(evt.OrderId, evt.Total));
            })
            .Then<PaymentReceived>(async (evt, state, ctx) =>
            {
                state.PaymentId = evt.PaymentId;
                state.PaymentConfirmed = true;
                await ctx.SendCommandAsync(new ReserveStock(state.OrderId, evt.Sku, evt.Quantity));
            })
            .Then<StockReserved>((evt, state, ctx) =>
            {
                state.StockReserved = true;
                ctx.Complete(); // saga is done
                return Task.CompletedTask;
            })
            .CompensateWith<PaymentFailed>(async (evt, state, ctx) =>
            {
                // Undo previous steps — run in reverse registration order.
                await ctx.SendCommandAsync(new CancelPayment(state.OrderId, evt.Reason));
            })
            .ExpireAfter(TimeSpan.FromMinutes(30))
            .OnTimeout(async (state, ctx) =>
            {
                await ctx.SendCommandAsync(new CancelPayment(state.OrderId, "Timed out"));
            });
    }
}
```

## Registering the saga

```csharp
services.AddOpinionatedEventingSagas();
services.AddSaga<OrderSaga>();
```

`AddSaga<T>` discovers the event types handled by the orchestrator and automatically registers `IEventHandler<TEvent>` for each one. No manual adapter registration is needed.

## How it works

### Starting a saga

When the framework receives an event registered with `StartWith<TEvent>`, it:

1. Creates a new `OrderSagaState` instance
2. Persists a `SagaState` row with `Status = Active`
3. Invokes the `StartWith` handler
4. Saves the updated state

The saga's `CorrelationId` is taken from the incoming event's `CorrelationId`. All subsequent commands and events in the chain carry the same `CorrelationId`, which is how the framework correlates them back to the right saga instance.

### Continuing a saga

When subsequent events arrive (registered with `Then<TEvent>`), the framework:

1. Looks up the existing `SagaState` by `CorrelationId`
2. Deserializes the persisted `State` JSON back into `TSagaState`
3. Invokes the handler
4. Serializes the updated state and persists it

### Completing a saga

Call `ctx.Complete()` inside any handler to mark the saga as `Completed`. No further events will be processed for this instance.

### ISagaContext

The `ISagaContext` passed to every handler provides:

```csharp
public interface ISagaContext
{
    Guid CorrelationId { get; }

    Task SendCommandAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand;

    Task PublishEventAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent;

    void Complete();
}
```

All messages sent through `ISagaContext` go through the outbox — they are written atomically with the saga state update.

## Timeout and compensation

### Setting a timeout

```csharp
builder.ExpireAfter(TimeSpan.FromMinutes(30));
// or — inject TimeProvider so the clock is controllable in tests
builder.ExpireAt(timeProvider.GetUtcNow().AddDays(1));
```

The `SagaTimeoutWorker` background service polls at `SagaOptions.TimeoutCheckInterval` (default: 30 seconds) for sagas whose `ExpiresAt` is in the past and `Status` is still `Active`. It then invokes the `OnTimeout` handler.

### Compensation

Register compensation handlers with `CompensateWith<TEvent>`:

```csharp
builder.CompensateWith<PaymentFailed>(async (evt, state, ctx) =>
{
    await ctx.SendCommandAsync(new CancelPayment(state.OrderId, evt.Reason));
});
```

When a compensation event arrives, the framework transitions the saga to `Compensating` and runs the handler. Compensation handlers run in **reverse registration order** — last registered, first executed — so you can undo steps in the correct sequence.

After compensation completes successfully, the saga transitions to `Completed`. On failure during compensation, it transitions to `Failed`.

## Saga status lifecycle

```
Active
  ├── ctx.Complete() ──────────────────────→ Completed
  ├── CompensateWith<T> handler invoked ──→ Compensating
  │     ├── success ───────────────────────→ Completed
  │     └── failure ───────────────────────→ Failed
  └── ExpiresAt reached ───────────────────→ TimedOut
        └── OnTimeout handler runs
              └── compensation if needed ──→ Completed / Failed
```

## Custom correlation

By default the framework correlates events to sagas via the `CorrelationId` in the message envelope. If you need to correlate by a property in the event payload, use `CorrelateBy`:

```csharp
builder.CorrelateBy<PaymentReceived>(evt => evt.OrderId.ToString());
```

## Persisting state: EF Core

Saga state is stored in the `saga_states` table. Configure it in your `DbContext`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplySagaStateConfiguration();
}
```

And register the EF Core store:

```csharp
services.AddOpinionatedEventingEntityFramework<AppDbContext>();
```

## Full registration example

```csharp
// Add the saga engine
services.AddOpinionatedEventingSagas(options =>
{
    options.TimeoutCheckInterval = TimeSpan.FromSeconds(30);
});

// Register each orchestrator
services.AddSaga<OrderSaga>();

// Register any choreography participants (see sagas-choreography.md)
services.AddSagaParticipant<FulfillmentParticipant>();
```

## Testing sagas

Use `InMemorySagaStateStore` from `OpinionatedEventing.Testing` to test saga logic without a database:

```csharp
var store = new InMemorySagaStateStore();
services.AddSingleton<ISagaStateStore>(store);

// Publish the starting event and assert on store.States
```

Use `FakeTimeProvider` to control the clock when testing timeout behaviour:

```csharp
var clock = new FakeTimeProvider();
services.AddSingleton<TimeProvider>(clock);

clock.Advance(TimeSpan.FromMinutes(31)); // trigger timeout
```
