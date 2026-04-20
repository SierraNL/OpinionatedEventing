using Samples.OrderService.Domain;

namespace Samples.OrderService.Application;

public sealed class PlaceOrderUseCase(IOrderRepository repository)
{
    public async Task<Guid> ExecuteAsync(string customerName, decimal total, CancellationToken ct = default)
    {
        var order = Order.Create(customerName, total);
        repository.Add(order);

        // SaveChanges triggers DomainEventInterceptor, which writes the OrderPlaced
        // domain event into the outbox atomically with the Order row.
        await repository.SaveChangesAsync(ct);

        return order.Id;
    }
}
