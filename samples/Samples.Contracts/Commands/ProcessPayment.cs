namespace Samples.Contracts.Commands;

public record ProcessPayment(Guid OrderId, string CustomerName, decimal Amount) : ICommand;
