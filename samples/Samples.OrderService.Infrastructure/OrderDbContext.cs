using Microsoft.EntityFrameworkCore;
using OpinionatedEventing.EntityFramework;
using Samples.OrderService.Domain;

namespace Samples.OrderService.Infrastructure;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.CustomerName).HasMaxLength(200).IsRequired();
            b.Property(o => o.Status).HasConversion<string>();
        });

        modelBuilder.ApplyOutboxConfiguration();
        modelBuilder.ApplySagaStateConfiguration();
    }
}
