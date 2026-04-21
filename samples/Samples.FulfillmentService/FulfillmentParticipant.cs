using Microsoft.Extensions.Logging;
using OpinionatedEventing.Sagas;
using Samples.Contracts.Events;

namespace Samples.FulfillmentService;

// Choreography participant: reacts directly to PaymentReceived without a central saga state.
// Reserves stock and raises StockReserved — OrderSaga observes this event to complete the flow.
public sealed class FulfillmentParticipant(
    FulfillmentDbContext db,
    ILogger<FulfillmentParticipant> logger) : ISagaParticipant<PaymentReceived>
{
    public async Task HandleAsync(PaymentReceived @event, ISagaContext ctx, CancellationToken cancellationToken)
    {
        logger.LogInformation("Reserving stock for order {OrderId}", @event.OrderId);

        // ctx.PublishEventAsync stages the StockReserved event in the outbox change tracker.
        await ctx.PublishEventAsync(new StockReserved(@event.OrderId), cancellationToken);

        // Commit the staged outbox message to the database.
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Stock reserved for order {OrderId}", @event.OrderId);
    }
}
