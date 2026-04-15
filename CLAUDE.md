# CLAUDE.md — OpinionatedEventing

This file gives Claude Code the context it needs to work effectively in this repository.

## Project overview

OpinionatedEventing is a suite of opinionated .NET 8/9/10 libraries for event-driven and command-driven messaging, targeting Azure Service Bus and RabbitMQ. The design philosophy is explicit, safe defaults over flexibility.

Full requirements are in [REQUIREMENTS.md](REQUIREMENTS.md). GitHub issues map one-to-one to library packages.

## Repository layout

```
src/
  OpinionatedEventing.Core/            # Abstractions only — no infra deps
  OpinionatedEventing.Outbox/          # Background dispatcher + IOutboxStore contract
  OpinionatedEventing.EntityFramework/ # EF Core outbox store + domain event interceptor + saga state
  OpinionatedEventing.Sagas/           # Orchestration + choreography engine
  OpinionatedEventing.AzureServiceBus/ # ASB transport
  OpinionatedEventing.RabbitMQ/        # RabbitMQ transport
  OpinionatedEventing.Aspire/          # Aspire AppHost extensions (local dev)
  OpinionatedEventing.Testing/         # Test helpers — not for production use
tests/
  *.Tests/   # xUnit unit / integration tests (no containers unless Category=Integration)
  *.Specs/   # Reqnroll BDD specs
```

## Key architectural decisions

- **Commands** go point-to-point (one handler, queue). **Events** fan-out (many handlers, topic/exchange). Enforced at compile time via `ICommand` / `IEvent` marker interfaces.
- **All outbound messages go through the outbox** — there is no direct broker publish path. `IPublisher` writes to `IOutboxStore` within the caller's EF Core `SaveChanges` transaction. `OutboxDispatcherWorker` dispatches async.
- **Domain events on aggregates** are harvested by `DomainEventInterceptor : SaveChangesInterceptor` and written to the outbox atomically. Aggregates never call `IPublisher`.
- **`IOutboxStore` is an abstraction** — the EF Core implementation lives in `OpinionatedEventing.EntityFramework`. Tests use `InMemoryOutboxStore` from `OpinionatedEventing.Testing`.
- **Sagas**: both orchestration (`SagaOrchestrator<TSagaState>`) and choreography (`ISagaParticipant<TEvent>`). Orchestration state stored in EF Core. Timeouts via `SagaTimeoutWorker` + `TimeProvider` (testable with fake clock).
- **Aspire**: both RabbitMQ and Azure Service Bus emulator supported locally. Transport switch is DI-only — no handler code changes.
- **Logging**: `Microsoft.Extensions.Logging.ILogger<T>` only — no Serilog/NLog dependency.
- **Tracing/Metrics**: `System.Diagnostics.ActivitySource` + `System.Diagnostics.Metrics` — no direct OTel package dependency in libraries.
- **Serialisation**: `System.Text.Json` only.

## Development conventions

- Target frameworks: `net8.0`, `net9.0`, `net10.0` (set globally in `Directory.Build.props`)
- `#nullable enable` everywhere; `TreatWarningsAsErrors=true`
- Central package management via `Directory.Packages.props` — never add a `Version` attribute to `<PackageReference>` in individual `.csproj` files
- All public APIs: `async Task`, `CancellationToken` on every async method
- No static mutable state; everything is DI-scoped
- XML doc comments on all `public` / `protected` members in `src/` projects
- No mocking frameworks in tests — use hand-written fakes or helpers from `OpinionatedEventing.Testing`
- Integration tests must be tagged `[Trait("Category", "Integration")]` so they are skipped in the non-container CI step

## Shell scripting

On Windows, use `pwsh` (PowerShell) for scripting tasks — never Python. Example: `pwsh -Command "..."`.

## Running locally

```bash
dotnet restore
dotnet build
dotnet test --filter "Category!=Integration"   # fast, no Docker needed
dotnet test --filter "Category=Integration"    # requires Docker (Testcontainers)
```

### dotnet CLI gotchas

- **`dotnet test` requires `--project`**, not a bare path: `dotnet test --project tests/Foo/Foo.csproj`
- **`dotnet build`/`test` with a path** does not need `--project`: `dotnet build tests/Foo/Foo.csproj`
- **`MSB3030 apphost.exe` on net9.0 in Specs projects** is a pre-existing environment issue — not caused by code changes, safe to ignore
- **`cref` attributes in XML docs** must use the fully qualified type name if the type lives in a different namespace within the same project (e.g. `<see cref="OpinionatedEventing.Outbox.IOutboxStore"/>` from within `OpinionatedEventing.Options`)

## Before committing

Always run `/review` on the staged changes before committing, and address any findings.

## Issue tracking

Each GitHub issue corresponds to a specific library. When implementing an issue, work on a branch named `issue/<number>-<short-description>` and open a PR targeting `main`.

Always use the MCP GitHub tools (`mcp__github__*`) for all GitHub interactions — never the `gh` CLI.
