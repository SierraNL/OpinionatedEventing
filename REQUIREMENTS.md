# OpinionatedEventing — Requirements

> Version: 1.0 · Status: Released  
> This document is the canonical source of requirements for the `OpinionatedEventing` library suite.  
> It reflects the as-built state of the v1.0 release.

---

## Table of Contents

1. [Goals & Non-Goals](#1-goals--non-goals)
2. [Library Structure](#2-library-structure)
3. [Core Abstractions](#3-core-abstractions-opinionatedeventingabstractions)
4. [Publishing & Consuming Rules](#4-publishing--consuming-rules)
5. [Outbox Pattern](#5-outbox-pattern-opinionatedeventingoutbox)
6. [EF Core Integration](#6-ef-core-integration-opinionatedeventingentityframework)
7. [Saga Support](#7-saga-support-opinionatedeventingsagas)
8. [DDD / Aggregate Support](#8-ddd--aggregate-support)
9. [Azure Service Bus Transport](#9-azure-service-bus-transport-opinionatedeventingazureservicebus)
10. [RabbitMQ Transport](#10-rabbitmq-transport-opinionatedeventingrabbitmq)
11. [Aspire / Local Development](#11-aspire--local-development-opinionatedeventingaspire)
12. [Observability](#12-observability)
13. [Documentation & Samples](#13-documentation--samples)
14. [Non-Functional Requirements](#14-non-functional-requirements)
15. [Testing Strategy](#15-testing-strategy)

---

## 1. Goals & Non-Goals

### Goals

- Provide a **small, cohesive** set of .NET 8+ libraries for event-driven and command-driven messaging.
- Enforce **correct patterns by default**: outbox-first publishing, strict command/event separation, transactional consistency with the business domain.
- Make it easy to build systems following **Domain-Driven Design** (aggregates, domain events).
- Support **two transports**: Azure Service Bus (cloud) and RabbitMQ (local/on-prem), switchable via DI registration with no handler code changes.
- Enable seamless **local development** using .NET Aspire with either RabbitMQ or the Azure Service Bus emulator (Docker).
- Integrate naturally with standard .NET ecosystem choices: `Microsoft.Extensions.DependencyInjection`, `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Logging`, and OpenTelemetry.
- Support **long-running workflows** (sagas) with orchestration, choreography, timeouts, and compensation.

### Non-Goals

- **Not** a generic broker abstraction layer — the library does not aim to support every possible messaging platform.
- **Not** an open framework — extension points are deliberate and limited; raw broker access is not exposed.
- **Not** a replacement for MassTransit, NServiceBus, Rebus, or similar frameworks — it intentionally covers less ground with fewer options.
- Saga state is only persisted via EF Core — other stores (Redis, Cosmos, raw SQL) are not supported in v1.
- No support for request/reply (RPC) patterns — commands are fire-and-forget to a single handler.
- No dynamic message routing — topics/queues are determined by message type at registration time.
- No support for .NET versions below 8.

---

## 2. Library Structure

| NuGet Package | Purpose |
|---|---|
| `OpinionatedEventing.Abstractions` | Pure contracts — `IEvent`, `ICommand`, `IPublisher`, `AggregateRoot`, handler interfaces. No infrastructure dependencies. |
| `OpinionatedEventing` | Runtime hosting — `MessageHandlerRunner`, `MessagingContext`, DI extensions, diagnostics, options. |
| `OpinionatedEventing.Outbox` | Outbox dispatcher background service and `IOutboxStore` contract. |
| `OpinionatedEventing.EntityFramework` | EF Core implementation of `IOutboxStore`, the domain-event interceptor, and saga state persistence. |
| `OpinionatedEventing.Sagas` | Saga orchestration and choreography engine. |
| `OpinionatedEventing.AzureServiceBus` | Azure Service Bus transport implementation. Health checks included. |
| `OpinionatedEventing.RabbitMQ` | RabbitMQ transport implementation. Health checks included. |
| `OpinionatedEventing.Aspire.RabbitMQ` | Aspire AppHost extension — RabbitMQ container resource. Reference from AppHost projects only. |
| `OpinionatedEventing.Aspire.AzureServiceBus` | Aspire AppHost extension — Azure Service Bus emulator resource. Reference from AppHost projects only. |
| `OpinionatedEventing.OpenTelemetry` | OpenTelemetry SDK integration — `TracerProviderBuilder` and `MeterProviderBuilder` extension methods. |
| `OpinionatedEventing.Testing` | Test helpers — in-memory fakes and builders for unit and integration tests. Not for production use. |

### Dependency rules

```
Abstractions  ←  OpinionatedEventing  ←  Outbox  ←  EntityFramework
Abstractions  ←  OpinionatedEventing  ←  Sagas   ←  EntityFramework
Abstractions  ←  OpinionatedEventing  ←  AzureServiceBus
Abstractions  ←  OpinionatedEventing  ←  RabbitMQ
Aspire.Hosting.RabbitMQ         ←  Aspire.RabbitMQ          (AppHost only)
Aspire.Hosting.Azure.ServiceBus ←  Aspire.AzureServiceBus   (AppHost only)
OpinionatedEventing  ←  OpenTelemetry
```

- `Abstractions` has **no** NuGet dependencies — pure .NET types only.
- `OpinionatedEventing` (root) depends on `Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Diagnostics.HealthChecks`, `Microsoft.Extensions.Logging.Abstractions`, and `Microsoft.Extensions.Options`.
- Transport packages depend on their respective broker client (`Azure.Messaging.ServiceBus`, `RabbitMQ.Client`) and `Microsoft.Extensions.Diagnostics.HealthChecks`.
- `Aspire.*` packages depend only on the matching `Aspire.Hosting.*` package and must not be referenced from application services.
- No library takes a hard dependency on a specific logging framework, serialisation library, or ORM other than EF Core in `EntityFramework`.

---

## 3. Core Abstractions (`OpinionatedEventing.Abstractions`)

### 3.1 Message Marker Interfaces

```csharp
/// <summary>
/// Marker interface for domain/integration events.
/// Events represent something that has already happened.
/// Implementations must be immutable (use record types).
/// </summary>
public interface IEvent { }

/// <summary>
/// Marker interface for commands.
/// Commands represent an instruction to perform an action.
/// Exactly one handler must be registered per command type.
/// Implementations must be immutable (use record types).
/// </summary>
public interface ICommand { }
```

- Both interfaces are intentionally empty markers — they exist to enforce compile-time correctness.
- Convention (enforced by a source analyzer in v2, documented guideline in v1): implementations should be `record` types.

### 3.2 Handler Interfaces

```csharp
public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken cancellationToken);
}
```

- Handlers are registered via DI. The framework scans registered assemblies at startup.
- Multiple `IEventHandler<T>` registrations for the same event type are allowed (fan-out).
- Exactly one `ICommandHandler<T>` per command type is enforced at startup — duplicate registrations throw an `InvalidOperationException`.

### 3.3 Publisher Interface

```csharp
public interface IPublisher
{
    /// <summary>Writes an event to the outbox. Transport delivery is async.</summary>
    Task PublishEventAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    /// <summary>Writes a command to the outbox. Transport delivery is async.</summary>
    Task SendCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand;
}
```

- `IPublisher` is the **only** way to send messages — there is no direct transport publish path.
- Calling `PublishEventAsync` or `SendCommandAsync` outside of a `SaveChanges` transaction is not supported when used with the EF Core integration (an exception is raised).

### 3.4 Outbox Message

```csharp
public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public string MessageType { get; init; }      // Stable registry identifier (FullName or [MessageType] override)
    public string Payload { get; init; }           // JSON-serialised message body
    public string MessageKind { get; init; }       // "Event" | "Command"
    public Guid CorrelationId { get; init; }
    public Guid? CausationId { get; init; }        // Id of the message that caused this one
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public int AttemptCount { get; set; }
    public string? Error { get; set; }
}
```

### 3.5 Outbox Store Interface

```csharp
public interface IOutboxStore
{
    Task SaveAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a transient dispatch failure without dead-lettering the message.
    /// Increments AttemptCount by one and stores the last error description.
    /// The message remains eligible for future dispatch attempts.
    /// </summary>
    Task IncrementAttemptAsync(Guid id, string error, CancellationToken cancellationToken = default);
}
```

### 3.6 Aggregate Root Base Class

```csharp
public abstract class AggregateRoot
{
    private readonly List<IEvent> _domainEvents = new();

    public IReadOnlyList<IEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IEvent domainEvent)
        => _domainEvents.Add(domainEvent);

    internal void ClearDomainEvents()
        => _domainEvents.Clear();
}
```

- Aggregates never call `IPublisher` directly.
- The EF Core interceptor (see §6) is responsible for harvesting `DomainEvents` and writing them to the outbox.

### 3.7 Additional Abstractions (`OpinionatedEventing.Abstractions`)

#### `IAggregateRoot`

```csharp
/// <summary>
/// Marks a class as a DDD aggregate root that collects domain events.
/// The EF Core interceptor detects this interface during SaveChanges to harvest
/// and outbox the accumulated events.
/// </summary>
/// <remarks>
/// Implement this interface directly when your aggregate already inherits from another
/// base class. For the common case with no existing base class, inherit from
/// AggregateRoot instead — it provides the standard implementation for free.
/// </remarks>
public interface IAggregateRoot
{
    IReadOnlyList<IEvent> DomainEvents { get; }

    /// <summary>Framework use only. Called by DomainEventInterceptor after harvest.</summary>
    void ClearDomainEvents();
}
```

#### `IConsumerPauseController`

```csharp
/// <summary>
/// Controls whether broker consumer workers should pause accepting new messages.
/// </summary>
/// <remarks>
/// The default implementation (NullConsumerPauseController) never pauses.
/// Register HealthCheckConsumerPauseController via
/// AddOpinionatedEventingHealthChecks().WithConsumerPause() to pause consumers
/// automatically when readiness probes become unhealthy.
/// </remarks>
public interface IConsumerPauseController
{
    bool IsPaused { get; }

    Task WhenStateChangedAsync(CancellationToken cancellationToken);
}
```

#### `IMessageHandlerRunner`

```csharp
/// <summary>
/// Dispatches a deserialized inbound message to its registered handler(s), initialising
/// IMessagingContext from the message envelope before any handler runs.
/// </summary>
/// <remarks>
/// Transport implementations resolve this service when a message is received from the broker.
/// A new DI scope is created per dispatch so that handler dependencies are scoped
/// to the lifetime of a single message. Part of the public API to allow custom transports.
/// </remarks>
public interface IMessageHandlerRunner
{
    Task RunAsync(
        string messageType,
        string messageKind,
        string payload,
        Guid correlationId,
        Guid? causationId,
        CancellationToken ct);
}
```

---

## 4. Publishing & Consuming Rules

### 4.1 Events

| Aspect | Rule |
|---|---|
| Direction | One sender → many potential receivers |
| Transport mapping | ASB: topic per event type; RabbitMQ: fanout/topic exchange per event type |
| Handler count | Zero or more `IEventHandler<T>` registrations |
| Delivery guarantee | At-least-once (via outbox) |
| Reply | None — fire and forget |

### 4.2 Commands

| Aspect | Rule |
|---|---|
| Direction | One sender → exactly one receiver |
| Transport mapping | ASB: queue per command type; RabbitMQ: direct queue per command type |
| Handler count | Exactly one `ICommandHandler<T>` — enforced at startup |
| Delivery guarantee | At-least-once (via outbox) |
| Reply | None — use events for acknowledgement |

### 4.3 Message Routing

- Topic/queue names are derived from the message type name by convention (e.g. `OrderPlaced` → `order-placed`).
- Naming convention is configurable via options.
- Explicit overrides are possible via `[MessageTopic("my-topic")]` / `[MessageQueue("my-queue")]` attributes defined in Core.

### 4.4 Correlation & Causation

- Every message carries a `CorrelationId` (Guid) that is propagated from the originating message through the entire chain.
- Every message carries a `CausationId` (nullable Guid) — the `Id` of the outbox message that triggered it.
- The framework sets these automatically when consuming a message and producing further outbox messages within the same handler.

### 4.5 Serialisation

- Messages are serialised to JSON using `System.Text.Json` (no Newtonsoft dependency).
- Serialiser options are configurable globally via DI options.
- Type discriminators are stored in `OutboxMessage.MessageType` — the framework resolves the CLR type on consumption.

---

## 5. Outbox Pattern (`OpinionatedEventing.Outbox`)

### 5.1 Write Path

1. Application code calls `IPublisher.PublishEventAsync` or `IPublisher.SendCommandAsync`.
2. `IPublisher` serialises the message and calls `IOutboxStore.SaveAsync` — this write participates in the **current ambient EF Core transaction**.
3. `SaveChanges` commits both the business data and the outbox row atomically.
4. No message is ever sent to the broker in the same call-stack as the business operation.

### 5.2 Dispatch Path (OutboxDispatcherWorker)

- A `BackgroundService` (`OutboxDispatcherWorker`) runs continuously.
- Polls `IOutboxStore.GetPendingAsync` on a configurable interval (default: 1 second).
- For each pending message: deserialises, forwards to the transport, calls `MarkProcessedAsync` on success.
- On transport failure: increments `AttemptCount`, calls `MarkFailedAsync` after `MaxAttempts` (default: 5).
- Batch size is configurable (default: 50 messages per poll cycle).
- Concurrency: configurable number of concurrent dispatch workers (default: 1).

### 5.3 Dead-Letter Handling

- Messages exceeding `MaxAttempts` are marked as dead-lettered in the outbox (`FailedAt` is set, `ProcessedAt` remains null).
- A separate `IOutboxMonitor` service (optional) can expose dead-letter counts via a health check and metrics.
- No automatic requeue of dead-lettered outbox messages — this requires explicit operator action.

### 5.4 Configuration

`AddOpinionatedEventing` returns an `OpinionatedEventingBuilder`; outbox registration is a separate call on that builder:

```csharp
services.AddOpinionatedEventing(options =>
{
    options.Outbox.PollInterval = TimeSpan.FromSeconds(1);
    options.Outbox.BatchSize = 50;
    options.Outbox.MaxAttempts = 5;
    options.Outbox.ConcurrentWorkers = 1;
})
.AddOutbox();
```

### 5.5 `ITransport` Contract (`OpinionatedEventing.Outbox`)

`ITransport` is the extension point through which transport packages plug into the outbox dispatcher. Transport packages (`AzureServiceBus`, `RabbitMQ`) register their implementation; application code must not call it directly.

```csharp
/// <summary>
/// Abstraction over the message broker transport layer.
/// Implemented by transport packages such as OpinionatedEventing.AzureServiceBus
/// and OpinionatedEventing.RabbitMQ.
/// Application code must not call this interface directly — use IPublisher instead.
/// </summary>
public interface ITransport
{
    Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
```

Custom transports must implement `ITransport` and register the implementation in DI.

---

## 6. EF Core Integration (`OpinionatedEventing.EntityFramework`)

### 6.1 Registration

The interceptor must be wired explicitly inside `AddDbContext`; `AddOpinionatedEventingEntityFramework` then registers the store and saga state implementations:

```csharp
services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseSqlServer(connectionString);
    options.AddInterceptors(sp.GetRequiredService<DomainEventInterceptor>());
});
services.AddOpinionatedEventingEntityFramework<AppDbContext>();
```

This registration:
- Registers `EfCoreOutboxStore : IOutboxStore` (scoped).
- Registers `DomainEventInterceptor : SaveChangesInterceptor`.
- Registers `EfCoreSagaStateStore : ISagaStateStore` (scoped).

### 6.2 Outbox Table

- `OutboxMessage` is mapped to an `outbox_messages` table.
- Provided via `OutboxMessageEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>`.
- Developer includes it in their `DbContext.OnModelCreating` via `modelBuilder.ApplyOutboxConfiguration()`.
- A migration extension `migrationBuilder.CreateOutboxTable()` is provided for convenience.

### 6.3 Domain Event Interceptor

- `DomainEventInterceptor` hooks into `SavingChangesAsync`.
- Scans all tracked entities for instances of `AggregateRoot`.
- For each aggregate with non-empty `DomainEvents`: serialises each event as an `OutboxMessage` and calls `SaveAsync` on the same `DbContext` transaction.
- Calls `ClearDomainEvents()` on the aggregate after harvest.
- The outbox rows are written **within the same `SaveChanges` call** — they are committed atomically with the entity changes.

### 6.4 Saga State Table

- `SagaState` is a generic-json table: `Id` (Guid), `SagaType` (string), `CorrelationId` (Guid), `State` (JSON), `Status` (enum: Active/Completed/Compensating/TimedOut/Failed), `CreatedAt`, `UpdatedAt`, `ExpiresAt` (nullable).
- Developer includes it via `modelBuilder.ApplySagaStateConfiguration()`.

### 6.5 Migrations

- The library never auto-applies migrations — this is the developer's responsibility.
- Extension methods (`CreateOutboxTable`, `DropOutboxTable`, `CreateSagaStateTable`) make migration authoring straightforward.

---

## 7. Saga Support (`OpinionatedEventing.Sagas`)

### 7.1 Overview

Two coordination styles are supported. Both use the same `CorrelationId` propagation mechanism and share the state store.

`ISagaStateStore` is defined in `OpinionatedEventing.Sagas` (the abstraction); the EF Core implementation (`EFCoreSagaStateStore`) is provided by `OpinionatedEventing.EntityFramework`. This mirrors the `IOutboxStore` / `EFCoreOutboxStore` split.

### 7.2 Orchestration

A central `SagaOrchestrator<TSagaState>` holds the saga's state and drives the workflow.

```csharp
public class OrderSaga : SagaOrchestrator<OrderSagaState>
{
    protected override void Configure(ISagaBuilder<OrderSagaState> builder)
    {
        builder
            .StartWith<OrderPlaced>(OnOrderPlaced)
            .Then<PaymentReceived>(OnPaymentReceived)
            .Then<StockReserved>(OnStockReserved)
            .OnTimeout(OnOrderExpired)
            .ExpireAfter(TimeSpan.FromMinutes(30))
            .CompensateWith<PaymentFailed>(OnPaymentFailed);
    }

    private Task OnOrderPlaced(OrderPlaced evt, OrderSagaState state, ISagaContext ctx)
    {
        state.OrderId = evt.OrderId;
        return ctx.SendCommandAsync(new ProcessPayment(evt.OrderId, evt.Amount));
    }
    // ...
}
```

- `SagaOrchestrator<TSagaState>` is registered via `services.AddSaga<OrderSaga>()`.
- State (`TSagaState`) must be a JSON-serialisable class.
- State is loaded from and persisted to the EF Core saga state table.
- The orchestrator sends commands and reacts to events; it never publishes events itself (that is the domain's responsibility).

### 7.3 Choreography

A lightweight participant that reacts to events without holding central state.

```csharp
public class FulfillmentParticipant : ISagaParticipant<PaymentReceived>
{
    public Task HandleAsync(PaymentReceived @event, ISagaContext ctx, CancellationToken ct)
        => ctx.SendCommandAsync(new ReserveStock(@event.OrderId));
}
```

- Choreography participants are registered via `services.AddSagaParticipant<FulfillmentParticipant>()`.
- No state table entry is created for choreography participants.

### 7.4 Correlation

- Sagas are identified by `CorrelationId`.
- The first event that starts a saga creates the saga state row with that `CorrelationId`.
- Subsequent events and commands carrying the same `CorrelationId` are routed to the existing saga instance.
- Correlation expression is configurable per-saga: `builder.CorrelateBy<PaymentReceived>(e => e.OrderId)`.

### 7.5 Timeouts

- Sagas can declare an expiry: `builder.ExpireAfter(TimeSpan)` or `builder.ExpireAt(DateTimeOffset)`.
- Expiry is stored in the saga state table (`ExpiresAt` column).
- A `SagaTimeoutWorker` (hosted service) polls for expired sagas and invokes the `OnTimeout` handler.
- On timeout, the saga transitions to the `TimedOut` status; compensation steps run if defined.
- Timeout check interval is configurable (default: 30 seconds).

### 7.6 Compensation

- Compensation handlers are registered with `CompensateWith<TEvent>(handler)`.
- Compensation runs in reverse registration order.
- If a compensation step fails, the saga transitions to `Failed` status and a dead-letter entry is created.

### 7.7 Saga Status Lifecycle

```
Active → Completed
Active → TimedOut → (compensation) → Completed | Failed
Active → Compensating → Completed | Failed
```

### 7.8 Registration

```csharp
services.AddOpinionatedEventingSagas(options =>
{
    options.TimeoutCheckInterval = TimeSpan.FromSeconds(30);
});
services.AddSaga<OrderSaga>();
services.AddSagaParticipant<FulfillmentParticipant>();
```

---

## 8. DDD / Aggregate Support

### 8.1 AggregateRoot

- Defined in `OpinionatedEventing.Abstractions` (no EF dependency in the base class).
- Provides `RaiseDomainEvent(IEvent)` to collect events during a business operation.
- Provides `DomainEvents` (read-only) for the interceptor to harvest.
- `ClearDomainEvents()` is internal — only the interceptor calls it.

### 8.2 Intended Usage Pattern

```csharp
public class Order : AggregateRoot
{
    public Guid Id { get; private set; }
    public OrderStatus Status { get; private set; }

    public static Order Place(Guid id, CustomerId customerId, IEnumerable<OrderLine> lines)
    {
        var order = new Order { Id = id, Status = OrderStatus.Pending };
        order.RaiseDomainEvent(new OrderPlaced(id, customerId, lines.ToList()));
        return order;
    }

    public void Cancel(string reason)
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidOperationException("Only pending orders can be cancelled.");
        Status = OrderStatus.Cancelled;
        RaiseDomainEvent(new OrderCancelled(Id, reason));
    }
}
```

### 8.3 Rules

- Aggregates **must not** take `IPublisher` as a dependency.
- Aggregates **must not** raise `ICommand` — only `IEvent`.
- Commands that should follow a domain event are the saga's or application service's responsibility.
- Developers who violate these rules by bypassing the outbox are responsible for the consequences — the framework does not block direct broker access (since the broker client is not exposed), but no helper APIs exist for it.

### 8.4 Correlation Propagation

- When a handler processes an inbound message, the framework sets an ambient `CorrelationId` and `CausationId` on an `IMessagingContext` (scoped DI service).
- When the EF interceptor harvests domain events, it reads from `IMessagingContext` and stamps the outbox rows accordingly.

---

## 9. Azure Service Bus Transport (`OpinionatedEventing.AzureServiceBus`)

### 9.1 Dependencies

- `Azure.Messaging.ServiceBus` (official Microsoft SDK)
- No other broker-specific dependencies.

### 9.2 Topology

| Message Kind | Azure Service Bus Resource |
|---|---|
| `IEvent` | Topic + subscription per consumer service |
| `ICommand` | Queue |

- Topic and queue names follow the same naming convention as §4.3.
- Subscriptions are named after the consuming service (configurable).

### 9.3 Registration

```csharp
services.AddAzureServiceBusTransport(options =>
{
    options.ConnectionString = "...";         // or use DefaultAzureCredential
    options.ServiceName = "order-service";    // used for subscription naming
    options.AutoCreateResources = false;      // true in dev, false by default
    options.EnableSessions = false;           // opt-in for ordered processing
});
```

### 9.4 Features

- **DefaultAzureCredential support**: connection string is optional; managed identity works out of the box.
- **Auto-create**: when `AutoCreateResources = true`, topics, queues, and subscriptions are created or updated at startup using the Service Bus management API.
- **Session-enabled queues**: opt-in per command type via `[SessionEnabled]` attribute; session ID defaults to `CorrelationId`.
- **Dead-letter handling**: messages that exceed the broker's delivery count are dead-lettered by the broker via `DeadLetterMessageAsync`; they are not written back to the outbox.
- **Graceful shutdown**: in-flight message processing completes before the host stops; `CancellationToken` is wired to the host lifetime.

---

## 10. RabbitMQ Transport (`OpinionatedEventing.RabbitMQ`)

### 10.1 Dependencies

- `RabbitMQ.Client` (official RabbitMQ .NET client)
- No other broker-specific dependencies.

### 10.2 Topology

| Message Kind | RabbitMQ Resource |
|---|---|
| `IEvent` | Topic exchange per event type + one queue per consumer binding |
| `ICommand` | Direct exchange + one queue per command type |

### 10.3 Registration

```csharp
services.AddRabbitMqTransport(options =>
{
    options.ConnectionString = "amqp://...";  // or Aspire service discovery
    options.ServiceName = "order-service";
    options.AutoDeclareTopology = true;       // on by default for local dev
});
```

### 10.4 Features

- **Auto-declare**: exchanges, queues, and bindings are declared at startup when `AutoDeclareTopology = true`. Safe to run repeatedly (idempotent declarations).
- **Aspire service discovery**: when running under Aspire, the connection string is resolved from `IConfiguration["ConnectionStrings:rabbitmq"]` automatically.
- **Consumer acknowledgement**: messages are ack'd only after the handler completes successfully; nack'd (with requeue=false) on unhandled exception — relies on broker dead-lettering.
- **Graceful shutdown**: consumer channels are closed cleanly on host stop.
- **Prefetch**: configurable prefetch count per consumer (default: 10).

---

## 11. Aspire / Local Development

### 11.1 Overview

Two separate AppHost-only packages add Aspire resource definitions for each supported local transport. Application services reference only the transport package — never an Aspire package.

| Package | Reference from |
|---|---|
| `OpinionatedEventing.Aspire.RabbitMQ` | AppHost project only |
| `OpinionatedEventing.Aspire.AzureServiceBus` | AppHost project only |

### 11.2 RabbitMQ Resource

```csharp
// In AppHost Program.cs
// dotnet add package OpinionatedEventing.Aspire.RabbitMQ
var rabbitmq = builder.AddRabbitMqMessaging("rabbitmq");

var orderService = builder.AddProject<Projects.OrderService>()
    .WithReference(rabbitmq);
```

- Starts a RabbitMQ Docker container with the management plugin enabled.
- Injects `ConnectionStrings__rabbitmq` into referenced projects.
- The `OpinionatedEventing.RabbitMQ` transport reads this connection string automatically via Aspire service discovery.

### 11.3 Azure Service Bus Emulator Resource

```csharp
// In AppHost Program.cs
// dotnet add package OpinionatedEventing.Aspire.AzureServiceBus
var asb = builder.AddAzureServiceBusEmulator("servicebus");

var orderService = builder.AddProject<Projects.OrderService>()
    .WithReference(asb);
```

- Starts the official Azure Service Bus emulator Docker container.
- Injects the emulator connection string into referenced projects.
- The `OpinionatedEventing.AzureServiceBus` transport detects the emulator connection string and configures itself accordingly (no managed identity, no TLS).
- Developers can run the **exact same handler and transport code** locally as in Azure — no code changes, only DI registration differs.

### 11.4 Transport Switching

Switching between RabbitMQ and Azure Service Bus requires changing only the DI registration:

```csharp
// Development (RabbitMQ or ASB emulator)
services.AddRabbitMQTransport(options => { ... });
// or
services.AddAzureServiceBusTransport(options => { ... });

// Production (Azure Service Bus)
services.AddAzureServiceBusTransport(options => { ... });
```

No handler, saga, aggregate, or outbox code changes.

### 11.5 Health Checks

Health checks are co-located with the package they check — each is an optional call on `IHealthChecksBuilder`:

| Extension | Package | Tags |
|---|---|---|
| `AddRabbitMqConnectivityHealthCheck()` | `OpinionatedEventing.RabbitMQ` | `live`, `broker` |
| `AddAzureServiceBusConnectivityHealthCheck()` | `OpinionatedEventing.AzureServiceBus` | `live`, `broker` |
| `AddOutboxBacklogHealthCheck()` | `OpinionatedEventing.Outbox` | `ready`, `outbox` |
| `AddSagaTimeoutBacklogHealthCheck()` | `OpinionatedEventing.Sagas` | `ready`, `saga` |
| `WithConsumerPause()` | `OpinionatedEventing` | — |

- Backlog checks report `Degraded` if their respective count exceeds a configurable threshold; they skip gracefully if the required service (`IOutboxMonitor`, `ISagaStateStore`) is not registered.
- `WithConsumerPause()` registers `HealthCheckConsumerPauseController` as `IConsumerPauseController` and `IHealthCheckPublisher`. Checks tagged `"pause"` trigger consumer suspension; `"ready"`-tagged backlog checks are intentionally excluded.
- Health checks integrate with `IHealthChecksBuilder` (standard ASP.NET Core health checks).

---

## 12. Observability

### 12.1 Logging

- All internal logging uses `Microsoft.Extensions.Logging.ILogger<T>` (abstractions package only).
- **No dependency on Serilog, NLog, or any other concrete logging library.**
- Consumers configure their own logging provider (Serilog, Application Insights, etc.) independently.
- Log levels: `Debug` for per-message trace, `Information` for startup/shutdown events, `Warning` for retries, `Error` for dead-letters and unhandled exceptions.

### 12.2 Distributed Tracing (OpenTelemetry)

- Traces use `System.Diagnostics.ActivitySource` internally — the library packages have **no** direct dependency on `OpenTelemetry.*`.
- Each transport creates spans for:
  - Message publish (outbox write)
  - Message dispatch (outbox → broker)
  - Message consume (broker → handler)
- `CorrelationId` and `CausationId` are propagated as baggage items.
- The `OpinionatedEventing.OpenTelemetry` package provides the OTel SDK integration. Consumers install it and wire it up:

```csharp
// dotnet add package OpinionatedEventing.OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddOpinionatedEventingInstrumentation())
    .WithMetrics(m => m.AddOpinionatedEventingMetrics());
```

### 12.3 Metrics

- Metrics use `System.Diagnostics.Metrics.Meter` internally — the library packages have **no** direct OTel dependency.
- Exposed meters (consumed via `AddOpinionatedEventingMetrics()` in `OpinionatedEventing.OpenTelemetry`):
  - `opinionatedeventing.outbox.pending` — gauge, current pending outbox message count
  - `opinionatedeventing.outbox.processed` — counter, total messages successfully dispatched
  - `opinionatedeventing.outbox.failed` — counter, total messages dead-lettered
  - `opinionatedeventing.publish.duration` — histogram, time from `PublishEventAsync` call to outbox write
  - `opinionatedeventing.dispatch.duration` — histogram, time from outbox poll to broker delivery
  - `opinionatedeventing.consume.duration` — histogram, handler execution time
  - `opinionatedeventing.saga.active` — gauge, currently active saga instances
  - `opinionatedeventing.saga.timed_out` — counter, total sagas that timed out

---

## 13. Documentation & Samples

### 13.1 XML Documentation

- All `public` and `protected` members in all packages carry `<summary>` XML doc comments.
- These are included in the NuGet packages so IntelliSense works out of the box.

### 13.2 Conceptual Guides (`docs/`)

The `docs/` folder at the repository root contains the following guides (Markdown):

| File | Topic |
|---|---|
| `getting-started.md` | Install packages, pick a transport, register handlers, publish a first event end-to-end |
| `commands-vs-events.md` | When to use a command vs an event; why the separation matters |
| `outbox-pattern.md` | How the outbox works internally, why direct publish is not available, failure handling |
| `sagas-orchestration.md` | Building an orchestrated saga with steps, timeout, and compensation |
| `sagas-choreography.md` | Using saga participants for event-driven choreography |
| `ddd-aggregates.md` | Defining aggregate roots, raising domain events, keeping aggregates free of infrastructure |
| `local-development.md` | Running locally with Aspire, RabbitMQ container, and Azure Service Bus emulator |
| `observability.md` | Wiring up logging, OTel traces, and metrics |

### 13.3 Sample Application (`samples/`)

A single sample solution demonstrating the library end-to-end:

**Scenario**: E-commerce order flow.

**Services:**
- `OrderService` — exposes an HTTP API; creates `Order` aggregates; raises `OrderPlaced`.
- `PaymentService` — handles `ProcessPayment` command; raises `PaymentReceived` or `PaymentFailed`.
- `FulfillmentService` — handles `ReserveStock` command; raises `StockReserved`.
- `NotificationService` — subscribes to `OrderPlaced`, `PaymentReceived`, `StockReserved` for email notifications.
- `Samples.AppHost` — .NET Aspire AppHost wiring all services together.

**Demonstrates:**
- `AggregateRoot` with `RaiseDomainEvent` (OrderService)
- Outbox write + dispatch (all services)
- `SagaOrchestrator` with timeout (30 min) and compensation on `PaymentFailed` (OrderService)
- `ISagaParticipant` choreography (FulfillmentService)
- Both transports: switch between RabbitMQ and Azure Service Bus emulator via `appsettings.Development.json`
- Health checks and OTel dashboard via Aspire

**Runnable with:**
```
cd samples/Samples.AppHost
dotnet run
```

---

## 14. Non-Functional Requirements

| Requirement | Specification |
|---|---|
| Target framework | .NET 8 (minimum); .NET 9 and .NET 10 also targeted |
| Async | All public APIs are `async Task`; `CancellationToken` on every async method |
| Thread safety | No shared mutable static state; all state is DI-scoped |
| Nullable | `#nullable enable` in all projects; no `null!` suppressions without comment |
| Immutability | `OutboxMessage` and all public message types use `init`-only setters |
| DI | Fully compatible with `Microsoft.Extensions.DependencyInjection`; no service locator |
| Versioning | Semantic versioning; each package versioned independently |
| Licensing | MIT |

---

## 15. Testing Strategy

### 15.1 Unit Tests

- Each library has a `*.Tests` project with xUnit.
- Core abstractions are tested in isolation with in-memory `IOutboxStore` / `ISagaStateStore` fakes.
- No mocking frameworks — use hand-written fakes to keep tests readable.

### 15.2 Integration Tests

| Scenario | Infrastructure |
|---|---|
| EF Core outbox (write + dispatch) | `Testcontainers` with a SQL Server or PostgreSQL container |
| RabbitMQ transport | `Testcontainers` with a RabbitMQ container |
| Azure Service Bus transport | Azure Service Bus emulator Docker container |
| Saga orchestration end-to-end | In-process, in-memory fake transport + EF Core SQLite |
| Saga timeout | Accelerated clock via `TimeProvider` abstraction |

### 15.3 Test Helpers (shipped as a separate `OpinionatedEventing.Testing` package)

- `InMemoryOutboxStore` — in-memory `IOutboxStore` for unit testing application code without EF Core.
- `InMemorySagaStateStore` — in-memory `ISagaStateStore` for testing saga state without EF Core.
- `FakePublisher` — records published events and sent commands for assertion.
- `FakeMessagingContext` — controllable `IMessagingContext` for unit tests.
- `FakeSagaContext` — controllable `ISagaContext` for saga unit tests.
- `FakeTimeProvider` — wraps `TimeProvider` with manual clock advance for timeout tests.
- `FakeConsumerPauseController` — controllable `IConsumerPauseController` for consumer pause tests.
- `FakeOutboxMonitor` — controllable `IOutboxMonitor` for backlog/health-check tests.
- `TestMessagingBuilder` — sets up a minimal DI container with fake transport for handler tests.

---

*End of requirements document.*
