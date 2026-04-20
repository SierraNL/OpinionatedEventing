namespace Samples.Contracts.Events;

public record StockReserved(Guid OrderId) : IEvent;
