using Microsoft.Extensions.Logging;
using Samples.Contracts.Commands;
using Samples.Contracts.Events;

namespace Samples.PaymentService;

// Simulates payment processing: always succeeds in this sample.
// In a real system this would call a payment gateway.
public sealed class ProcessPaymentHandler(
    IPublisher publisher,
    PaymentDbContext db,
    ILogger<ProcessPaymentHandler> logger) : ICommandHandler<ProcessPayment>
{
    public async Task HandleAsync(ProcessPayment command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Processing payment of {Amount:C} for order {OrderId} (customer: {CustomerName})",
            command.Amount, command.OrderId, command.CustomerName);

        var paymentId = Guid.NewGuid();

        await publisher.PublishEventAsync(
            new PaymentReceived(command.OrderId, paymentId, command.Amount),
            cancellationToken);

        // Commit the staged outbox message together with any future business state.
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Payment {PaymentId} accepted for order {OrderId}", paymentId, command.OrderId);
    }
}
