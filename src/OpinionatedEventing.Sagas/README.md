# OpinionatedEventing.Sagas

Saga orchestration and choreography engine for OpinionatedEventing. Supports:

- **Orchestration** — stateful, long-running workflows with compensation and timeouts (`SagaOrchestrator<TSagaState>`)
- **Choreography** — lightweight, stateless event reaction (`ISagaParticipant<TEvent>`)

Saga state is persisted via `OpinionatedEventing.EntityFramework`. Timeouts are driven by `TimeProvider` and are fully testable with a fake clock.

## Installation

```
dotnet add package OpinionatedEventing.Sagas
dotnet add package OpinionatedEventing.EntityFramework
```

## Registration

```csharp
builder.Services
    .AddOpinionatedEventingSagas()
    .AddSaga<OrderSaga>()
    .AddSagaParticipant<NotificationParticipant>();
```

## Orchestration example

Override `Configure(ISagaBuilder<TSagaState> builder)` to wire up event handlers, compensation, and timeouts using the fluent `ISagaBuilder` API:

```csharp
public class OrderSagaState
{
    public Guid OrderId { get; set; }
    public bool PaymentReceived { get; set; }
}

public class OrderSaga : SagaOrchestrator<OrderSagaState>
{
    protected override void Configure(ISagaBuilder<OrderSagaState> builder)
    {
        builder
            .StartWith<OrderPlaced>(OnOrderPlaced)
            .Then<PaymentReceived>(OnPaymentReceived)
            .CompensateWith<PaymentFailed>(OnPaymentFailed)
            .OnTimeout(OnExpired)
            .ExpireAfter(TimeSpan.FromDays(3));
    }

    private Task OnOrderPlaced(OrderPlaced @event, OrderSagaState state, ISagaContext ctx)
    {
        state.OrderId = @event.OrderId;
        return ctx.SendCommandAsync(new ProcessPayment(@event.OrderId, @event.Total));
    }

    private Task OnPaymentReceived(PaymentReceived @event, OrderSagaState state, ISagaContext ctx)
    {
        state.PaymentReceived = true;
        ctx.Complete();
        return ctx.SendCommandAsync(new ShipOrder(state.OrderId));
    }

    private Task OnPaymentFailed(PaymentFailed @event, OrderSagaState state, ISagaContext ctx)
        => ctx.PublishEventAsync(new OrderCancelled(state.OrderId));

    private Task OnExpired(OrderSagaState state, ISagaContext ctx)
        => ctx.PublishEventAsync(new OrderExpired(state.OrderId));
}
```

By default, messages are correlated using a `CorrelationId` property on the event. Override with `.CorrelateBy<TEvent>(e => ...)` for custom correlation.

## Choreography example

```csharp
public class NotificationParticipant : ISagaParticipant<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced @event, IPublisher publisher, CancellationToken ct = default)
        => publisher.PublishEventAsync(new SendWelcomeEmail(@event.CustomerId), ct);
}
```

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)
