using Samples.OrderService.Domain;

namespace Samples.OrderService.Application;

public interface IOrderRepository
{
    void Add(Order order);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
