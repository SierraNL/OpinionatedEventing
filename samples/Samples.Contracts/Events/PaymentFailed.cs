namespace Samples.Contracts.Events;

public record PaymentFailed(Guid OrderId, string Reason) : IEvent;
