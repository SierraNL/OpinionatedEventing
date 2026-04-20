using Microsoft.Extensions.Logging;
using Samples.Contracts.Commands;

namespace Samples.PaymentService;

public sealed class CancelPaymentHandler(ILogger<CancelPaymentHandler> logger) : ICommandHandler<CancelPayment>
{
    public Task HandleAsync(CancelPayment command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Payment cancelled for order {OrderId}: {Reason}",
            command.OrderId, command.Reason);
        return Task.CompletedTask;
    }
}
