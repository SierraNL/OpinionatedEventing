# GitHub Copilot Instructions — OpinionatedEventing

## What this repo is

A suite of opinionated .NET 8/9/10 messaging libraries. Full design spec: [REQUIREMENTS.md](../REQUIREMENTS.md).

## Non-negotiable rules — always follow these

1. **No direct broker publish.** All messages go through `IPublisher` → `IOutboxStore` → `OutboxDispatcherWorker` → transport. Never call the broker SDK directly from application or library code.

2. **Strict ICommand / IEvent separation.** `PublishEventAsync<T>` only accepts `T : IEvent`. `SendCommandAsync<T>` only accepts `T : ICommand`. Do not blur this line.

3. **Aggregates raise events, never commands.** `AggregateRoot.RaiseDomainEvent(IEvent)` only. The EF Core interceptor harvests and routes them — aggregates must not receive `IPublisher` as a dependency.

4. **EF Core interceptor is the only harvest point.** Domain events on aggregates are written to the outbox inside `SavingChangesAsync`. Do not add alternative publish paths.

5. **`System.Text.Json` only.** No `Newtonsoft.Json` anywhere.

6. **`ILogger<T>` only for logging.** No Serilog, NLog, or other concrete logging libraries as dependencies.

7. **No OTel package dependencies in libraries.** Use `System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics`. Consumers bring their own OTel SDK.

8. **Central package versioning.** Never add a `Version` attribute to `<PackageReference>` in a `.csproj`. All versions live in `Directory.Packages.props`.

9. **No mocking frameworks in tests.** Use `InMemoryOutboxStore`, `FakePublisher`, `FakeMessagingContext` from `OpinionatedEventing.Testing`, or hand-written fakes.

10. **Integration tests must be tagged.** `[Trait("Category", "Integration")]` on any test that needs Docker / Testcontainers. This lets CI skip them on fast runs.

## Code style

- `#nullable enable` — no `null!` suppressions without a comment explaining why
- `async Task` + `CancellationToken` on every async public method
- No shared mutable static state — all state must be DI-scoped
- XML `<summary>` doc comments on all `public` and `protected` members in `src/` projects
- Target frameworks: `net8.0;net9.0;net10.0` (set in `Directory.Build.props`, do not override per project)

## Project structure quick reference

| Project | What goes here |
|---|---|
| `Core` | Interfaces, base types, attributes — zero infra deps |
| `Outbox` | `OutboxDispatcherWorker`, `ITransport` internal interface |
| `EntityFramework` | EF outbox table, `DomainEventInterceptor`, saga state table |
| `Sagas` | `SagaOrchestrator<T>`, `ISagaParticipant<T>`, `SagaTimeoutWorker` |
| `AzureServiceBus` | ASB-specific `ITransport` impl |
| `RabbitMQ` | RabbitMQ-specific `ITransport` impl |
| `Aspire` | AppHost extensions, health checks |
| `Testing` | `InMemoryOutboxStore`, `FakePublisher`, `TestMessagingBuilder` |
| `*.Tests` | xUnit unit + integration tests |
| `*.Specs` | Reqnroll BDD feature files + step definitions |
