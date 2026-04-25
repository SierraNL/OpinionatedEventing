# AGENTS.md — OpinionatedEventing

A suite of opinionated .NET 8/9/10 libraries for event-driven and command-driven messaging, targeting Azure Service Bus and RabbitMQ. The design philosophy is explicit, safe defaults over flexibility.

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

## Non-negotiable design rules

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

- `#nullable enable` everywhere — no `null!` suppressions without a comment explaining why
- `TreatWarningsAsErrors=true`
- `async Task` + `CancellationToken` on every async public method
- No shared mutable static state — all state must be DI-scoped
- XML `<summary>` doc comments on all `public` and `protected` members in `src/` projects
- Target frameworks: `net8.0;net9.0;net10.0` (set in `Directory.Build.props` — do not override per project)
- C# naming and formatting rules are enforced via [`.editorconfig`](.editorconfig): file-scoped namespaces, Allman braces, `_camelCase` private fields, `s_camelCase` private static fields, explicit types preferred over `var`

## Running locally

```bash
dotnet restore
dotnet build
dotnet test -- --filter-trait "Category!=Integration"   # fast, no Docker needed
dotnet test -- --filter-trait "Category=Integration"    # requires Docker (Testcontainers)
```

> **Note:** Use `-- --filter-trait` (MTP syntax), not `--filter`. The CI pipeline uses the same syntax.

### dotnet CLI notes

- **`dotnet test` requires `--project`**, not a bare path: `dotnet test --project tests/Foo/Foo.csproj`
- **`dotnet build`/`test` with a path** does not need `--project`: `dotnet build tests/Foo/Foo.csproj`
- **`cref` attributes in XML docs** must use the fully qualified type name when the type lives in a different namespace within the same project (e.g. `<see cref="OpinionatedEventing.Outbox.IOutboxStore"/>`)

### Shell scripting

On Windows, use `pwsh` (PowerShell) for scripting tasks — never Python. Example: `pwsh -Command "..."`.

## Issue tracking & branching

The GitHub repository is **`SierraNL/OpinionatedEventing`**.

When implementing an issue, always fetch and reset to `origin/main` before creating a branch named `issue/<number>-<short-description>`, then open a PR targeting `main`.
