using OpinionatedEventing.Sagas;

namespace OpinionatedEventing.Sagas.Tests.TestSupport;

// --- Events ---

internal sealed record OrderPlaced : IEvent
{
    public Guid CorrelationId { get; init; }
    public decimal Amount { get; init; }
}

internal sealed record PaymentReceived : IEvent
{
    public Guid CorrelationId { get; init; }
}

internal sealed record PaymentFailed : IEvent
{
    public Guid CorrelationId { get; init; }
}

internal sealed record StockReserved : IEvent
{
    public Guid OrderId { get; init; } // custom correlation key
    public Guid CorrelationId { get; init; }
}

internal sealed record OrderExpired : IEvent
{
    public Guid CorrelationId { get; init; }
}

// --- Commands ---

internal sealed record ProcessPayment : ICommand
{
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
}

internal sealed record ReserveStock : ICommand
{
    public Guid OrderId { get; init; }
}

internal sealed record RefundPayment : ICommand
{
    public Guid OrderId { get; init; }
}

// --- Saga state ---

internal sealed class OrderSagaState
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public bool PaymentProcessed { get; set; }
}

// --- Happy-path saga: OrderPlaced → PaymentReceived → Complete ---

internal sealed class OrderSaga : SagaOrchestrator<OrderSagaState>
{
    protected override void Configure(ISagaBuilder<OrderSagaState> builder)
    {
        builder
            .StartWith<OrderPlaced>(OnOrderPlaced)
            .Then<PaymentReceived>(OnPaymentReceived)
            .CompensateWith<PaymentFailed>(OnPaymentFailed);
    }

    private Task OnOrderPlaced(OrderPlaced evt, OrderSagaState state, ISagaContext ctx)
    {
        state.OrderId = evt.CorrelationId;
        state.Amount = evt.Amount;
        return ctx.SendCommandAsync(new ProcessPayment { OrderId = state.OrderId, Amount = state.Amount });
    }

    private Task OnPaymentReceived(PaymentReceived evt, OrderSagaState state, ISagaContext ctx)
    {
        state.PaymentProcessed = true;
        ctx.Complete();
        return Task.CompletedTask;
    }

    private Task OnPaymentFailed(PaymentFailed evt, OrderSagaState state, ISagaContext ctx)
    {
        ctx.Complete();
        return Task.CompletedTask;
    }
}

// --- Timeout saga ---

internal sealed class TimedOrderSagaState
{
    public bool TimedOut { get; set; }
}

internal sealed class TimedOrderSaga : SagaOrchestrator<TimedOrderSagaState>
{
    protected override void Configure(ISagaBuilder<TimedOrderSagaState> builder)
    {
        builder
            .StartWith<OrderPlaced>((_, _, _) => Task.CompletedTask)
            .OnTimeout(OnTimeout)
            .ExpireAfter(TimeSpan.FromMinutes(30));
    }

    private Task OnTimeout(TimedOrderSagaState state, ISagaContext ctx)
    {
        state.TimedOut = true;
        ctx.Complete();
        return Task.CompletedTask;
    }
}

// --- Custom-correlation saga ---

internal sealed class CustomCorrelationSagaState
{
    public Guid OrderId { get; set; }
}

internal sealed class CustomCorrelationSaga : SagaOrchestrator<CustomCorrelationSagaState>
{
    protected override void Configure(ISagaBuilder<CustomCorrelationSagaState> builder)
    {
        builder
            .StartWith<OrderPlaced>((evt, state, _) => { state.OrderId = evt.CorrelationId; return Task.CompletedTask; })
            .Then<StockReserved>((_, _, ctx) => { ctx.Complete(); return Task.CompletedTask; })
            .CorrelateBy<StockReserved>(e => e.OrderId.ToString());
    }
}

// --- Timeout saga that throws ---

internal sealed class ThrowingTimeoutSagaState { }

internal sealed class ThrowingTimeoutSaga : SagaOrchestrator<ThrowingTimeoutSagaState>
{
    protected override void Configure(ISagaBuilder<ThrowingTimeoutSagaState> builder)
    {
        builder
            .StartWith<OrderPlaced>((_, _, _) => Task.CompletedTask)
            .OnTimeout((_, _) => Task.FromException(new InvalidOperationException("timeout failure")))
            .ExpireAfter(TimeSpan.FromMinutes(30));
    }
}

// --- Choreography participant ---

internal sealed class StockParticipant : ISagaParticipant<StockReserved>
{
    public List<StockReserved> Handled { get; } = new();

    public Task HandleAsync(StockReserved @event, ISagaContext ctx, CancellationToken cancellationToken)
    {
        Handled.Add(@event);
        return ctx.SendCommandAsync(new ReserveStock { OrderId = @event.OrderId });
    }
}
