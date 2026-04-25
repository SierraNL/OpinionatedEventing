using Microsoft.EntityFrameworkCore;

namespace OpinionatedEventing.EntityFramework.Tests.TestSupport;

internal sealed class SqliteTestDbContext : DbContext
{
    public SqliteTestDbContext(DbContextOptions<SqliteTestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyOutboxConfiguration(Database.ProviderName);
        modelBuilder.ApplySagaStateConfiguration(Database.ProviderName);
    }
}
