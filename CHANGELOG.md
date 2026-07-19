# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `PackageTags` metadata on all NuGet packages for better discoverability on nuget.org
- CODEOWNERS file and pull request template to streamline contributions
- `OpinionatedEventing.CloudEvents`, `OpinionatedEventing.CloudEvents.AzureServiceBus`, and `OpinionatedEventing.CloudEvents.RabbitMQ` — opt-in CloudEvents 1.0 structured envelope for events, enabled per transport via `UseCloudEventsEnvelope()`. Commands are unaffected; services that don't opt in are unaffected.
- `IServiceBusMessageEnvelope` / `IRabbitMQMessageEnvelope` injectable envelope abstractions on the Azure Service Bus and RabbitMQ transports, replacing the previously inline message build/parse logic

### Removed
- .NET 9 support — .NET 9 (STS) reached end of support on 2026-05-12. Target frameworks are now `net8.0` and `net10.0`.

## [0.9.0] - 2026-05-08

Initial pre-release of the OpinionatedEventing library suite for .NET 8+.

### Added

**Packages**
- `OpinionatedEventing.Abstractions` — core contracts: `IEvent`, `ICommand`, `IEventHandler<T>`, `ICommandHandler<T>`, `IAggregateRoot`
- `OpinionatedEventing` — main bus infrastructure and message dispatching
- `OpinionatedEventing.Outbox` — transactional outbox pattern with background dispatcher, retention cleanup, and exponential retry backoff
- `OpinionatedEventing.EntityFramework` — EF Core integration with outbox persistence, claim-column locking for duplicate-dispatch prevention, SQLite `DateTimeOffset` value converters, `EFCoreOutboxTransactionGuard`, and `EFCoreOutboxMonitor`
- `OpinionatedEventing.Sagas` — saga orchestration, choreography, timeout support with `TimeProvider`, and claim-column locking on timeout acquisition
- `OpinionatedEventing.AzureServiceBus` — Azure Service Bus transport
- `OpinionatedEventing.RabbitMQ` — RabbitMQ transport with publisher confirms, mandatory routing, DLX/DLQ topology, channel pool, and async `IHostedService` connection warm-up
- `OpinionatedEventing.Aspire.AzureServiceBus` — Aspire health checks for Azure Service Bus local development
- `OpinionatedEventing.Aspire.RabbitMQ` — Aspire health checks for RabbitMQ local development
- `OpinionatedEventing.OpenTelemetry` — structured logging, distributed tracing, and metrics via OpenTelemetry
- `OpinionatedEventing.Testing` — test helper utilities for in-process event bus testing

**Features**
- `MessageKind` enum to distinguish commands from events at the infrastructure level
- Configurable Aspire connection-string key per transport
- Stable message-type identifier (replaces `AssemblyQualifiedName`) and exposed `MessageId`
- `MessageHandlerRegistry` for compile-time-safe handler resolution
- Auto-registration of `IEventHandler<T>` adapters for sagas and choreography participants
- Cached compiled handler dispatchers for reduced per-message allocation
- Receive-only services can omit `IOutboxStore` — no outbox dependency required
- GitVersion integration for automatic SemVer from git tags
- Aspire CLI support and clone-and-run sample experience
- NuGet trusted publishing via GitHub Actions on version tags

### Changed
- Split monolithic Aspire package into `OpinionatedEventing.Aspire.AzureServiceBus` and `OpinionatedEventing.Aspire.RabbitMQ` for independent transport adoption
- Split `OpinionatedEventing.Core` into `OpinionatedEventing.Abstractions` (contracts) and `OpinionatedEventing` (infrastructure) to allow contract-only references
- Replaced `ServiceCollectionAccessor` with `MessageHandlerRegistry` for handler resolution
- Replaced `AssemblyQualifiedName`-based type keys with stable identifiers safe across refactors and renames

### Fixed
- `MessageId` now threaded as `CausationId` on outbound messages to establish correct parent-child trace chain
- Consumer pause scoped to the `"pause"` tag to prevent oscillation when multiple tags are present
- Pre-1.0 correctness and consistency bundle: various edge-case fixes across transport, outbox, and saga layers
- MSB3030 build error when .NET 10 SDK builds `net9.0` test projects in the AppHost

[Unreleased]: https://github.com/SierraNL/OpinionatedEventing/compare/v0.9.0...HEAD
[0.9.0]: https://github.com/SierraNL/OpinionatedEventing/releases/tag/v0.9.0
