using Samples.Contracts.Events;

namespace Samples.OrderService.Domain;

public class Order : AggregateRoot
{
    public Guid Id { get; private set; }
    public string CustomerName { get; private set; } = string.Empty;
    public decimal Total { get; private set; }
    public OrderStatus Status { get; private set; }

    // Required by EF Core.
    private Order() { }

    public static Order Create(string customerName, decimal total)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = customerName,
            Total = total,
            Status = OrderStatus.Pending,
        };

        // The DomainEventInterceptor harvests this event during SaveChanges
        // and writes it to the outbox atomically — no direct broker publish.
        order.RaiseDomainEvent(new OrderPlaced(order.Id, customerName, total));
        return order;
    }
}
