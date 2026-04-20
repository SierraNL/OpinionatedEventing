namespace Samples.Contracts.Commands;

public record CancelPayment(Guid OrderId, string Reason) : ICommand;
