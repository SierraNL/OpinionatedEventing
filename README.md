# OpinionatedEventing

[![CI](https://github.com/SierraNL/OpinionatedEventing/actions/workflows/ci.yml/badge.svg)](https://github.com/SierraNL/OpinionatedEventing/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/SierraNL/OpinionatedEventing/branch/main/graph/badge.svg)](https://codecov.io/gh/SierraNL/OpinionatedEventing)
[![License: MIT](https://img.shields.io/github/license/SierraNL/OpinionatedEventing)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4)](https://dotnet.microsoft.com)

A suite of opinionated .NET libraries for event-driven and command-driven messaging, targeting Azure Service Bus and RabbitMQ. Designed for developers who want correctness and safe defaults over maximum flexibility.

## What it does

- **Outbox-first publishing** — events and commands are written atomically with your business data, then dispatched asynchronously. There is no direct broker publish path.
- **Commands vs events, enforced** — `ICommand` routes point-to-point (one handler); `IEvent` fans out (many handlers). The compiler and DI container enforce this distinction.
- **DDD aggregate roots** — raise domain events inside aggregate methods; the `DomainEventInterceptor` harvests and outboxes them automatically on `SaveChanges`.
- **Sagas** — orchestration (`SagaOrchestrator<TSagaState>`) with state, timeouts, and compensation, plus lightweight choreography (`ISagaParticipant<TEvent>`).
- **Transport-agnostic** — swap Azure Service Bus for RabbitMQ (or vice versa) by changing a single DI call. No handler code changes.
- **Aspire-ready** — one-line AppHost extensions spin up RabbitMQ or the Azure Service Bus emulator locally.
- **Observable** — structured logging via `ILogger<T>`, distributed tracing via `ActivitySource`, metrics via `System.Diagnostics.Metrics`.

## Packages

| Package | NuGet | Purpose |
|---|---|---|
| `OpinionatedEventing.Core` | [![NuGet](https://img.shields.io/nuget/v/OpinionatedEventing.Core.svg)](https://www.nuget.org/packages/OpinionatedEventing.Core) | Abstractions only — `IEvent`, `ICommand`, `IPublisher`, `AggregateRoot` |
| `OpinionatedEventing.Outbox` | [![NuGet](https://img.shields.io/nuget/v/OpinionatedEventing.Outbox.svg)](https://www.nuget.org/packages/OpinionatedEventing.Outbox) | `OutboxDispatcherWorker`, `IOutboxStore` contract |
| `OpinionatedEventing.EntityFramework` | [![NuGet](https://img.shields.io/nuget/v/OpinionatedEventing.EntityFramework.svg)](https://www.nuget.org/packages/OpinionatedEventing.EntityFramework) | EF Core outbox store, `DomainEventInterceptor`, saga state |
| `OpinionatedEventing.Sagas` | [![NuGet](https://img.shields.io/nuget/v/OpinionatedEventing.Sagas.svg)](https://www.nuget.org/packages/OpinionatedEventing.Sagas) | Orchestration and choreography engine |
| `OpinionatedEventing.AzureServiceBus` | [![NuGet](https://img.shields.io/nuget/v/OpinionatedEventing.AzureServiceBus.svg)](https://www.nuget.org/packages/OpinionatedEventing.AzureServiceBus) | Azure Service Bus transport |
| `OpinionatedEventing.RabbitMQ` | [![NuGet](https://img.shields.io/nuget/v/OpinionatedEventing.RabbitMQ.svg)](https://www.nuget.org/packages/OpinionatedEventing.RabbitMQ) | RabbitMQ transport |
| `OpinionatedEventing.Aspire` | [![NuGet](https://img.shields.io/nuget/v/OpinionatedEventing.Aspire.svg)](https://www.nuget.org/packages/OpinionatedEventing.Aspire) | Aspire AppHost extensions and health checks |
| `OpinionatedEventing.Testing` | [![NuGet](https://img.shields.io/nuget/v/OpinionatedEventing.Testing.svg)](https://www.nuget.org/packages/OpinionatedEventing.Testing) | In-memory stores and fakes for tests |

## Quick start

```csharp
// Define messages
public record OrderPlaced(Guid OrderId, decimal Total) : IEvent;
public record ProcessPayment(Guid OrderId, decimal Amount) : ICommand;

// Handle events
public class NotificationHandler : IEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced @event, CancellationToken ct) => ...;
}

// Wire up (Program.cs)
services
    .AddOpinionatedEventing()
    .AddHandlersFromAssemblies(Assembly.GetExecutingAssembly())
    .AddOutbox();

services.AddOpinionatedEventingEntityFramework<AppDbContext>();
services.AddRabbitMQTransport(o => { o.ServiceName = "my-service"; });

// Publish (within a SaveChanges transaction)
await publisher.PublishEventAsync(new OrderPlaced(id, total), ct);
await db.SaveChangesAsync(ct);
```

## Documentation

| Guide | Description |
|---|---|
| [Getting Started](docs/getting-started.md) | Install, configure, and publish your first event |
| [Commands vs Events](docs/commands-vs-events.md) | When to use each and why the separation matters |
| [Outbox Pattern](docs/outbox-pattern.md) | How the outbox works internally, retry, and dead-letters |
| [Saga Orchestration](docs/sagas-orchestration.md) | Stateful workflows with timeouts and compensation |
| [Saga Choreography](docs/sagas-choreography.md) | Lightweight event-driven coordination with `ISagaParticipant` |
| [DDD Aggregates](docs/ddd-aggregates.md) | Aggregate roots, domain events, and the interceptor |
| [Local Development](docs/local-development.md) | Running locally with Aspire, RabbitMQ, and ASB emulator |
| [Observability](docs/observability.md) | Logging, distributed tracing, and metrics |

## Requirements

.NET 8, 9, or 10. See [REQUIREMENTS.md](REQUIREMENTS.md) for the full specification.

## License

MIT — see [LICENSE](LICENSE).
