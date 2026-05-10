# Contributing to OpinionatedEventing

Thank you for your interest in contributing. This guide covers everything you need to get the development environment running, run tests, and submit a pull request.

## Prerequisites

| Tool | Required version | Notes |
|------|-----------------|-------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0.100 or later 10.x patch | `global.json` enforces this; `rollForward: latestMinor` allows newer patches |
| [Docker](https://docs.docker.com/get-docker/) | Any recent stable version | Required only for integration tests (Testcontainers) and the Aspire sample |
| [Aspire workload](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/setup-tooling) | Matches your SDK | Required only to run the sample: `dotnet workload install aspire` |
| Git | Any recent version | |

The libraries target `net8.0`, `net9.0`, and `net10.0`. CI builds all three, but for local development the .NET 10 SDK is sufficient — it can build all target frameworks.

## Clone and build

```bash
git clone https://github.com/SierraNL/OpinionatedEventing.git
cd OpinionatedEventing

dotnet restore LibrariesOnly.slnf
dotnet build LibrariesOnly.slnf --configuration Release
```

`LibrariesOnly.slnf` is a solution filter that includes only `src/` and `tests/` — it excludes the Aspire sample, which has heavier dependencies. Use it for day-to-day library development.

## Running tests

Tests are split into two categories: **unit / BDD** (fast, no Docker) and **integration** (requires Docker via Testcontainers).

### Unit and BDD tests

```bash
dotnet test LibrariesOnly.slnf -- --filter-not-trait "Category=Integration"
```

These run on all three target frameworks and complete in seconds. No external services are needed.

> **MTP syntax note:** The project uses [Microsoft.Testing.Platform](https://learn.microsoft.com/en-us/testing-platform/overview) (MTP). On .NET 10, test-runner arguments are passed directly. On .NET 8 and 9, bridge mode is active and arguments must follow a `--` separator — as shown above. Always use `--filter-not-trait` / `--filter-trait` rather than `--filter`.

### Integration tests

```bash
dotnet test LibrariesOnly.slnf -- --filter-trait "Category=Integration"
```

Integration tests require Docker to be running. Testcontainers will pull and start containers (RabbitMQ, SQL Server) automatically. They are tagged with `[Trait("Category", "Integration")]` and deliberately excluded from the fast run above.

In CI, integration tests run on `net10.0` only to prevent Docker container name conflicts when all three framework legs run concurrently on the same runner. Locally you can run them against any target framework.

### Running a single test project

```bash
dotnet test tests/OpinionatedEventing.Tests/ -- --filter-not-trait "Category=Integration"
```

## Running the Aspire sample

The sample demonstrates the full stack: four microservices (Order, Payment, Fulfillment, Notification) wired together with RabbitMQ transport and PostgreSQL persistence.

```bash
dotnet workload install aspire   # one-time setup
dotnet run --project samples/Samples.AppHost
```

Docker must be running. The Aspire dashboard URL is printed to the terminal on startup.

## Branching

Create branches off `main` using the format:

```
issue/<number>-<short-description>
```

Examples: `issue/42-fix-outbox-retry`, `issue/99-add-rabbitmq-health-check`.

Always start from a clean `main`:

```bash
git fetch origin
git reset --hard origin/main
git checkout -b issue/<number>-<short-description>
```

## Commit message style

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope>): <short description>
```

Common types: `feat`, `fix`, `docs`, `test`, `refactor`, `ci`, `chore`.

Examples:
```
feat(outbox): add configurable retry delay
fix(rabbitmq): handle connection reset on channel close
docs: update README quick-start example
test(sagas): add choreography happy-path spec
```

## Pull request checklist

Before opening a PR, confirm:

- [ ] All new and changed lines in `src/` are covered by a test — CI enforces this via `codecov/patch`
- [ ] Unit and BDD tests pass: `dotnet test LibrariesOnly.slnf -- --filter-not-trait "Category=Integration"`
- [ ] Integration tests pass locally (if you touched transport or EF Core code): `dotnet test LibrariesOnly.slnf -- --filter-trait "Category=Integration"`
- [ ] Public and protected members in `src/` have XML `<summary>` doc comments
- [ ] No `Version` attributes added to `<PackageReference>` elements — all versions live in `Directory.Packages.props`
- [ ] `CHANGELOG.md` has an entry under `[Unreleased]` describing the change

Open the PR targeting `main`:

```bash
git push -u origin issue/<number>-<short-description>
gh pr create --repo SierraNL/OpinionatedEventing --title "..." --body "..."
```

Wait for all CI checks to pass before requesting a review.

## Code style

`.editorconfig` enforces formatting automatically. Key rules:

| Rule | Value |
|------|-------|
| Namespaces | File-scoped (`namespace Foo;`) |
| Braces | Allman style (opening brace on its own line) |
| Private fields | `_camelCase` |
| Private static fields | `s_camelCase` |
| Type declarations | Explicit types preferred over `var` |
| Nullability | `#nullable enable` everywhere; no `null!` suppressions without an explanatory comment |
| Warnings | `TreatWarningsAsErrors=true` — zero warnings in CI |
| Async methods | `async Task` return type + `CancellationToken` parameter on every public async method |
| Serialization | `System.Text.Json` only — no Newtonsoft.Json |
| Logging | `ILogger<T>` only — no concrete logging libraries as dependencies |
| Mocking in tests | Not allowed — use `InMemoryOutboxStore`, `FakePublisher`, and `FakeMessagingContext` from `OpinionatedEventing.Testing`, or hand-write fakes |

Run `dotnet build` before committing — a warning-free build is the minimum bar.
