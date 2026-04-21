using Microsoft.Extensions.Logging;
using Samples.Contracts.Events;

namespace Samples.NotificationService;

public sealed class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger) : IEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced @event, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[Notification] Order {OrderId} placed — customer: {CustomerName}, total: {Total:C}",
            @event.OrderId, @event.CustomerName, @event.Total);
        return Task.CompletedTask;
    }
}

public sealed class PaymentReceivedHandler(ILogger<PaymentReceivedHandler> logger) : IEventHandler<PaymentReceived>
{
    public Task HandleAsync(PaymentReceived @event, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[Notification] Payment {PaymentId} received for order {OrderId} — amount: {Amount:C}",
            @event.PaymentId, @event.OrderId, @event.Amount);
        return Task.CompletedTask;
    }
}

public sealed class StockReservedHandler(ILogger<StockReservedHandler> logger) : IEventHandler<StockReserved>
{
    public Task HandleAsync(StockReserved @event, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[Notification] Stock reserved for order {OrderId} — ready for dispatch",
            @event.OrderId);
        return Task.CompletedTask;
    }
}
