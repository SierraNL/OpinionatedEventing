using Microsoft.EntityFrameworkCore;
using OpinionatedEventing.EntityFramework;

namespace Samples.PaymentService;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyOutboxConfiguration();
    }
}
