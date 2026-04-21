using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpinionatedEventing.EntityFramework;
using Samples.OrderService.Application;

namespace Samples.OrderService.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrderServiceInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        // Register IOutboxStore, ISagaStateStore, and DomainEventInterceptor first
        // so they are available when the DbContext options factory runs.
        services.AddOpinionatedEventingEntityFramework<OrderDbContext>();

        services.AddDbContext<OrderDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            // DomainEventInterceptor harvests aggregate domain events during SaveChanges
            // and writes them to the outbox in the same transaction.
            options.AddInterceptors(sp.GetRequiredService<DomainEventInterceptor>());
        });

        services.AddScoped<IOrderRepository, EfOrderRepository>();
        return services;
    }
}
