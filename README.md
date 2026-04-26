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
| `OpinionatedEventing.Abstractions` | [![NuGet](https://img.shields.io/nuget/v/OpinionatedEventing.Abstractions.svg)](https://www.nuget.org/packages/OpinionatedEventing.Abstractions) | Pure contracts — `IEvent`, `ICommand`, `IPublisher`, `AggregateRoot`, `IOutboxStore` |
| `OpinionatedEventing` | [![NuGet](https://img.shields.io/nuget/v/OpinionatedEventing.svg)](https://www.nuget.org/packages/OpinionatedEventing) | Runtime hosting — `MessageHandlerRunner`, DI extensions, diagnostics, options |
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
| [Idempotency](docs/idempotency.md) | At-least-once delivery, duplicate handling, and inbox-pattern guidance |

## Requirements

.NET 8, 9, or 10. See [REQUIREMENTS.md](REQUIREMENTS.md) for the full specification.

## Why I built it

Over the years, I've worked with quite a few .NET frameworks around eventing or messaging.
Before one is picked, it's always a big discussion—especially with paid solutions.
Personally, I feel that the best ones are paid now, and given the companies I worked for in the past, they end up being very pricey for just a few .NET libraries.

So I was happy when Microsoft had a plan to create something:
https://github.com/dotnet/aspnetcore/issues/53219

But if you scroll to the end, it never happened ☹️
Mostly due to the concern that if there is a Microsoft library for it, it will kill the open‑source community around that topic.

That may be true, but I really like de‑facto standard solutions instead of picking from 10+ options.
(That's why I switched from Java to .NET in the first place, way back 😉)

Every now and then, this plan popped back into my mind—to build it myself, the way Microsoft usually does it. It doesn’t have to tackle every use case, but at least 80–90%.
So: an opinionated library, targeting only a few brokers commonly used with .NET, using Entity Framework for outbox and sagas, and encouraging good DDD practices.

I always had this “How hard can it be?” mindset, but I lacked the spare time to actually build it.
Thanks to the rise of GenAI, this became really doable. So Claude and I took the plunge.

Yes, Claude did most of the typing, but I reviewed all the code, steered where needed, and I’ve been a C# developer for 20+ years.

## Future plans

I hope this is enough for now, scope‑wise.
Ideas are always welcome—I can’t oversee the entire .NET world, nor am I an expert on DDD or eventing/messaging.

There can always be bugs, vulnerabilities, and dependency updates; I’ll pick them up.
The plan is to move along with the .NET versions that are in support, together with the matching EF Core versions.

## License

MIT — see [LICENSE](LICENSE).
