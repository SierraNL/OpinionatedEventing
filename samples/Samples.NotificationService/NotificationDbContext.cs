using Microsoft.EntityFrameworkCore;
using OpinionatedEventing.EntityFramework;

namespace Samples.NotificationService;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Outbox store is required by the framework even though this service never publishes.
        modelBuilder.ApplyOutboxConfiguration();
    }
}
