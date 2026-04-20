using OpinionatedEventing.Sagas;
using Samples.Contracts.Commands;
using Samples.Contracts.Events;

namespace Samples.OrderService.Application;

// Orchestrates the full order flow: payment → stock reservation → completion.
// Uses CorrelateBy(evt => evt.OrderId) because the events carry OrderId as the
// business correlation key rather than a generic CorrelationId property.
public sealed class OrderSaga : SagaOrchestrator<OrderSagaState>
{
    protected override void Configure(ISagaBuilder<OrderSagaState> builder)
    {
        builder
            .CorrelateBy<OrderPlaced>(evt => evt.OrderId.ToString())
            .CorrelateBy<PaymentReceived>(evt => evt.OrderId.ToString())
            .CorrelateBy<PaymentFailed>(evt => evt.OrderId.ToString())
            .CorrelateBy<StockReserved>(evt => evt.OrderId.ToString())

            .StartWith<OrderPlaced>(async (evt, state, ctx) =>
            {
                state.OrderId = evt.OrderId;
                state.CustomerName = evt.CustomerName;
                state.Total = evt.Total;
                await ctx.SendCommandAsync(new ProcessPayment(evt.OrderId, evt.CustomerName, evt.Total));
            })

            .Then<PaymentReceived>((evt, state, ctx) =>
            {
                state.PaymentId = evt.PaymentId;
                return Task.CompletedTask;
            })

            // FulfillmentService reacts to PaymentReceived as a choreography participant
            // and raises StockReserved — OrderSaga only needs to observe the outcome.
            .Then<StockReserved>((evt, state, ctx) =>
            {
                ctx.Complete();
                return Task.CompletedTask;
            })

            // Compensation: payment rejected — cancel any in-progress payment.
            .CompensateWith<PaymentFailed>(async (evt, state, ctx) =>
            {
                await ctx.SendCommandAsync(new CancelPayment(state.OrderId, evt.Reason));
            })

            .ExpireAfter(TimeSpan.FromMinutes(30))
            .OnTimeout(async (state, ctx) =>
            {
                await ctx.SendCommandAsync(new CancelPayment(state.OrderId, "Order timed out after 30 minutes"));
            });
    }
}
