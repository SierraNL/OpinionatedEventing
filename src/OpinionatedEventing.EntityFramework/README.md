# OpinionatedEventing.EntityFramework

EF Core integration for OpinionatedEventing. Provides:

- **`EfOutboxStore`** — `IOutboxStore` backed by EF Core; outbox writes participate in `SaveChanges` transactions automatically.
- **`DomainEventInterceptor`** — `SaveChangesInterceptor` that harvests domain events from `AggregateRoot` entities and writes them to the outbox atomically.
- **Saga state persistence** — EF Core-backed `ISagaStateStore` for `OpinionatedEventing.Sagas`.

## Installation

```
dotnet add package OpinionatedEventing.EntityFramework
```

## Registration

```csharp
builder.Services.AddOpinionatedEventingEntityFramework<AppDbContext>();
```

Add the outbox and saga tables to your `DbContext`:

```csharp
public class AppDbContext : DbContext
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<SagaState> SagaStates => Set<SagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyOutboxConfiguration();
        modelBuilder.ApplySagaStateConfiguration();
    }
}
```

Then add a migration:

```
dotnet ef migrations add AddOpinionatedEventing
dotnet ef database update
```

## Domain events on aggregates

Extend `AggregateRoot` and raise domain events without calling `IPublisher` directly:

```csharp
public class Order : AggregateRoot
{
    public Order(Guid id)
    {
        Id = id;
        RaiseDomainEvent(new OrderCreated(id));
    }
}
```

`DomainEventInterceptor` harvests `RaiseDomainEvent` calls during `SaveChangesAsync` and writes them to the outbox in the same transaction.

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)
