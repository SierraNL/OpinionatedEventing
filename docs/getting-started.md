# Getting Started

This guide walks you through installing OpinionatedEventing, picking a transport, registering handlers, and publishing your first event end-to-end.

## Prerequisites

- .NET 8, 9, or 10
- An Azure Service Bus namespace **or** a RabbitMQ broker (or run either locally via [Aspire](local-development.md))
- Entity Framework Core (only required if the service **publishes** messages or uses sagas — receive-only services do not need it)

## 1. Install packages

The packages you need depend on what the service does.

**Domain / contracts assembly** (only needs marker interfaces and base types):

```
dotnet add package OpinionatedEventing.Abstractions
```

**Service host** (handles or publishes messages — needs DI registration and runtime):

```
dotnet add package OpinionatedEventing

# Pick one transport:
dotnet add package OpinionatedEventing.AzureServiceBus
dotnet add package OpinionatedEventing.RabbitMQ
```

**Service that publishes** (also sends events or commands):

```
dotnet add package OpinionatedEventing
dotnet add package OpinionatedEventing.Outbox
dotnet add package OpinionatedEventing.EntityFramework

# Pick one transport:
dotnet add package OpinionatedEventing.AzureServiceBus
dotnet add package OpinionatedEventing.RabbitMQ
```

For sagas add:

```
dotnet add package OpinionatedEventing.Sagas
```

For local development with .NET Aspire:

```
dotnet add package OpinionatedEventing.Aspire
```

## 2. Define your messages

Messages are plain C# records. Implement `IEvent` for domain / integration events and `ICommand` for commands.

```csharp
using OpinionatedEventing;

// An event is something that happened — broadcast to all interested parties.
public record OrderPlaced(Guid OrderId, decimal Total) : IEvent;

// A command is an instruction directed at one specific handler.
public record ProcessPayment(Guid OrderId, decimal Amount) : ICommand;
```

See [Commands vs Events](commands-vs-events.md) for guidance on when to use each.

## 3. Implement handlers

```csharp
using OpinionatedEventing;

// Multiple handlers can subscribe to the same event.
public class OrderNotificationHandler : IEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced @event, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Order {@event.OrderId} placed for {@event.Total:C}");
        return Task.CompletedTask;
    }
}

// Exactly one handler per command type — enforced at startup.
public class PaymentCommandHandler : ICommandHandler<ProcessPayment>
{
    public Task HandleAsync(ProcessPayment command, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Processing payment of {command.Amount:C} for order {command.OrderId}");
        return Task.CompletedTask;
    }
}
```

## 4. Configure your DbContext (publishing services only)

Skip this step if your service only handles messages and never publishes. Add `OutboxMessage` to your `DbContext` and wire up the `DomainEventInterceptor`.

```csharp
using Microsoft.EntityFrameworkCore;
using OpinionatedEventing.EntityFramework;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyOutboxConfiguration();   // outbox_messages table
        modelBuilder.ApplySagaStateConfiguration(); // saga_states table (if using sagas)
    }
}
```

Generate and apply an EF migration:

```bash
dotnet ef migrations add AddOutbox
dotnet ef database update
```

## 5. Register services

Wire everything up in `Program.cs` (or `Startup.cs`).

```csharp
using OpinionatedEventing;
using OpinionatedEventing.EntityFramework;

// --- Core ---
var builder = services
    .AddOpinionatedEventing()
    .AddHandlersFromAssemblies(Assembly.GetExecutingAssembly())
    .AddOutbox();

// --- EF Core outbox store ---
services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlServer(connectionString);
    options.AddInterceptors(sp.GetRequiredService<DomainEventInterceptor>());
});
services.AddOpinionatedEventingEntityFramework<AppDbContext>();

// --- Transport (choose one) ---

// Azure Service Bus:
services.AddAzureServiceBusTransport(options =>
{
    options.ConnectionString = builder.Configuration["AzureServiceBus:ConnectionString"];
    options.ServiceName = "order-service"; // becomes the subscription name
});

// RabbitMQ:
services.AddRabbitMQTransport(options =>
{
    options.ConnectionString = builder.Configuration["RabbitMQ:ConnectionString"];
    options.ServiceName = "order-service";
});
```

You only need one transport — swap them by changing DI registration. No handler code changes required.

## 6. Publish your first event

Inject `IPublisher` wherever you need to emit events or send commands.

```csharp
public class OrderService(IPublisher publisher, AppDbContext db)
{
    public async Task PlaceOrderAsync(decimal total, CancellationToken ct)
    {
        var order = new Order { Total = total };
        db.Orders.Add(order);

        // Write the event to the outbox within the same transaction.
        await publisher.PublishEventAsync(new OrderPlaced(order.Id, total), ct);

        // One SaveChanges call commits both the Order row and the outbox row atomically.
        await db.SaveChangesAsync(ct);
    }
}
```

The `OutboxDispatcherWorker` background service picks up the pending message and forwards it to the broker. Your `OrderNotificationHandler` is called on the consumer side.

> **Why not publish directly to the broker?** The outbox pattern guarantees that a message is never lost even if the broker is temporarily unavailable. See [Outbox Pattern](outbox-pattern.md) for details.

## 7. (Optional) Configure the outbox dispatcher

Tune polling and retry behaviour via `OpinionatedEventingOptions`:

```csharp
services.AddOpinionatedEventing(options =>
{
    options.Outbox.PollInterval = TimeSpan.FromSeconds(2);
    options.Outbox.BatchSize = 100;
    options.Outbox.MaxAttempts = 5;
    options.Outbox.ConcurrentWorkers = 2;
});
```

## Next steps

| Topic | Guide |
|---|---|
| When to use events vs commands | [Commands vs Events](commands-vs-events.md) |
| How the outbox works internally | [Outbox Pattern](outbox-pattern.md) |
| Long-running workflows | [Saga Orchestration](sagas-orchestration.md) |
| Lightweight event-driven choreography | [Saga Choreography](sagas-choreography.md) |
| Aggregates and domain events | [DDD Aggregates](ddd-aggregates.md) |
| Local dev with Aspire | [Local Development](local-development.md) |
| Logging, tracing, and metrics | [Observability](observability.md) |
