namespace Samples.Contracts.Events;

public record PaymentReceived(Guid OrderId, Guid PaymentId, decimal Amount) : IEvent;
