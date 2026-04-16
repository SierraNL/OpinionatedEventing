using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpinionatedEventing;

namespace OpinionatedEventing.EntityFramework.Tests.TestSupport;

internal sealed class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyOutboxConfiguration();
        modelBuilder.ApplySagaStateConfiguration();
        modelBuilder.ApplyConfiguration(new TestOrderEntityTypeConfiguration());
    }
}

internal sealed class OrderPlaced : IEvent
{
    public Guid OrderId { get; init; }
}

internal sealed class TestOrder : AggregateRoot
{
    public Guid Id { get; init; }

    public static TestOrder Place(Guid id)
    {
        var order = new TestOrder { Id = id };
        order.RaiseDomainEvent(new OrderPlaced { OrderId = id });
        return order;
    }

    public void Cancel()
        => RaiseDomainEvent(new OrderPlaced { OrderId = Id });
}

internal sealed class TestOrderEntityTypeConfiguration : IEntityTypeConfiguration<TestOrder>
{
    public void Configure(EntityTypeBuilder<TestOrder> builder)
    {
        builder.ToTable("test_orders");
        builder.HasKey(o => o.Id);
        builder.Ignore(o => o.DomainEvents);
    }
}
