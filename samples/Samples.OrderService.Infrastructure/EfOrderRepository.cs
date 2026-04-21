using Samples.OrderService.Application;
using Samples.OrderService.Domain;

namespace Samples.OrderService.Infrastructure;

internal sealed class EfOrderRepository(OrderDbContext db) : IOrderRepository
{
    public void Add(Order order) => db.Orders.Add(order);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
