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
        modelBuilder.ApplyOutboxConfiguration(Database.ProviderName);
        modelBuilder.ApplySagaStateConfiguration(Database.ProviderName);
    }
}
```

### Supported databases

| Provider | Notes |
|----------|-------|
| **SQL Server** | Fully supported. `DateTimeOffset` columns stored natively. |
| **PostgreSQL** | Fully supported. `DateTimeOffset` columns stored natively. |
| **SQLite** | ⚠️ **Not for production use.** SQLite has no native `DateTimeOffset` type. When `Database.ProviderName` contains `"Sqlite"`, the library automatically applies a value converter that stores all `DateTimeOffset` columns as UTC ticks (`long` / `INTEGER`), preserving sort order on the pending-message and saga-timeout indexes. Useful for local development, testing, and demos. |

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
