# OpinionatedEventing

Runtime hosting package for the OpinionatedEventing library suite. Contains `MessageHandlerRunner`, `MessagingContext`, DI extensions (`AddOpinionatedEventing`), diagnostics, and options. Depends on `OpinionatedEventing.Abstractions`.

Reference this package from composition-root and infrastructure assemblies. Domain and application assemblies that only need marker interfaces or base types should reference `OpinionatedEventing.Abstractions` instead.

## Installation

```
dotnet add package OpinionatedEventing
```

## What's included

| Type | Purpose |
|---|---|
| `MessageHandlerRunner` | Dispatches inbound messages to their registered handler(s) |
| `MessagingContext` | Scoped correlation/causation context populated by the transport layer |
| `NullConsumerPauseController` | No-op default — consumers always run at full speed |
| `ServiceCollectionExtensions` | `AddOpinionatedEventing()` DI registration entry point |
| `OpinionatedEventingBuilder` | Fluent builder for `AddHandlersFromAssemblies` and further configuration |
| `EventingTelemetry` | `ActivitySource` and `Meter` name constants for OTel SDK registration |
| `OpinionatedEventingOptions` | Top-level options (serializer, outbox settings) |
| `OutboxOptions` | Outbox dispatcher tuning — poll interval, batch size, max attempts, concurrency |

## Quick start

```csharp
builder.Services
    .AddOpinionatedEventing()
    .AddHandlersFromAssemblies(Assembly.GetExecutingAssembly())
    .AddOutbox();          // OpinionatedEventing.Outbox
```

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)
