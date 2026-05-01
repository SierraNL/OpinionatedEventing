# Saga Choreography

Choreography is the lighter-weight alternative to orchestration. Instead of a central coordinator that sends commands and waits for responses, each service independently reacts to the events it cares about — no central workflow class, no shared state machine.

`ISagaParticipant<TEvent>` is the building block for choreography in OpinionatedEventing.

## When to use choreography

Use choreography when:

- Each step is genuinely independent — the services do not need to share state
- You want maximum decoupling: adding a new service is a matter of subscribing to an existing event
- The workflow has no meaningful timeout or compensation requirements
- The flow is simple enough to reason about without a central view

Use [orchestration](sagas-orchestration.md) when you need state, timeouts, compensation, or a single place to read the full workflow.

## ISagaParticipant vs IEventHandler

Both `ISagaParticipant<TEvent>` and `IEventHandler<TEvent>` react to events. The difference is context:

| | `IEventHandler<T>` | `ISagaParticipant<T>` |
|---|---|---|
| Access to `ISagaContext` | No | Yes |
| Can send commands via outbox | Via `IPublisher` injection | Via `ctx.SendCommandAsync()` |
| Can publish events via outbox | Via `IPublisher` injection | Via `ctx.PublishEventAsync()` |
| Has CorrelationId from chain | Via `IMessagingContext` | Via `ctx.CorrelationId` |

Use `ISagaParticipant<TEvent>` when you need to send commands or publish events as part of a choreographed flow. Use `IEventHandler<TEvent>` for side-effects that don't produce outbound messages.

## Implementing a participant

```csharp
public class FulfillmentParticipant : ISagaParticipant<PaymentReceived>
{
    private readonly IInventoryService _inventory;

    public FulfillmentParticipant(IInventoryService inventory)
    {
        _inventory = inventory;
    }

    public async Task HandleAsync(
        PaymentReceived @event,
        ISagaContext ctx,
        CancellationToken cancellationToken)
    {
        // Reserve stock in response to a payment being received.
        var result = await _inventory.TryReserveAsync(@event.Sku, @event.Quantity, cancellationToken);

        if (result.Success)
        {
            await ctx.PublishEventAsync(
                new StockReserved(ctx.CorrelationId, @event.Sku, result.ReservedQuantity),
                cancellationToken);
        }
        else
        {
            await ctx.PublishEventAsync(
                new StockReservationFailed(ctx.CorrelationId, @event.Sku, result.Reason),
                cancellationToken);
        }
    }
}
```

## Registering a participant

```csharp
services.AddOpinionatedEventingSagas();
services.AddSagaParticipant<FulfillmentParticipant>();
```

`AddSagaParticipant<T>` scans the `ISagaParticipant<TEvent>` interface, extracts the event type, registers the correct `IEventHandler<TEvent>` subscription automatically, and wires it to the saga dispatcher. The participant is resolved from DI per message — constructor injection works normally.

## ISagaContext in choreography

The `ISagaContext` available in `HandleAsync` provides:

```csharp
public interface ISagaContext
{
    Guid CorrelationId { get; }      // propagated from the incoming event

    Task SendCommandAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand;

    Task PublishEventAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent;

    void Complete(); // no-op in a stateless participant, but available
}
```

All messages sent through `ISagaContext` go through the outbox. If the handler throws, none of them are dispatched.

## Choreography vs orchestration: a comparison

Consider the e-commerce flow:

```
OrderPlaced
  └── FulfillmentParticipant reacts → sends ReserveStock command
        └── ReserveStock handled → raises StockReserved
              └── NotificationHandler reacts → sends email
```

With orchestration an `OrderSaga` drives every step and persists state between them. With choreography each service reacts in isolation — there is no saga state table and no coordinator.

The choreography version is simpler to write and deploy, but harder to debug across services because there is no single place that holds the full picture of "where is this order in the flow?"

**Rule of thumb:** start with choreography. Introduce orchestration when you need compensation, timeout, or a single audit trail.

## Testing participants

`ISagaParticipant<TEvent>.HandleAsync` receives an `ISagaContext`. Use `FakeSagaContext` from `OpinionatedEventing.Testing` to assert on sent commands and published events without any broker interaction:

```csharp
using OpinionatedEventing.Testing;

var ctx = new FakeSagaContext { CorrelationId = Guid.NewGuid() };
var participant = new FulfillmentParticipant(new FakeInventoryService());

await participant.HandleAsync(
    new PaymentReceived(orderId, paymentId, "SKU-123", 2),
    ctx,
    CancellationToken.None);

Assert.Single(ctx.PublishedEvents.OfType<StockReserved>());
Assert.False(ctx.IsCompleted); // Complete() was not called
```

`FakeSagaContext` captures commands and events in `SentCommands` and `PublishedEvents` (both `IReadOnlyList<object>`). Use `.OfType<T>()` to filter by type, consistent with `FakePublisher`.
