namespace Samples.Contracts.Events;

public record OrderPlaced(Guid OrderId, string CustomerName, decimal Total) : IEvent;
