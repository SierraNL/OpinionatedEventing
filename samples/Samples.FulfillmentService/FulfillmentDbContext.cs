using Microsoft.EntityFrameworkCore;
using OpinionatedEventing.EntityFramework;

namespace Samples.FulfillmentService;

public sealed class FulfillmentDbContext(DbContextOptions<FulfillmentDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyOutboxConfiguration();
        // saga_states table required because ISagaDispatcher depends on ISagaStateStore,
        // even though this service only registers choreography participants (no orchestrators).
        modelBuilder.ApplySagaStateConfiguration();
    }
}
