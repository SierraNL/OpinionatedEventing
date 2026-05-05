# OpinionatedEventing.OpenTelemetry

OpenTelemetry SDK integration for OpinionatedEventing. Provides `TracerProviderBuilder` and `MeterProviderBuilder` extension methods to opt in to distributed tracing and metrics emitted by the library.

The core library emits telemetry via `System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics` with no direct OpenTelemetry SDK dependency. This package wires those sources into your OTel pipeline.

## Installation

```
dotnet add package OpinionatedEventing.OpenTelemetry
```

## Registration

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddOpinionatedEventingInstrumentation())
    .WithMetrics(metrics => metrics
        .AddOpinionatedEventingMetrics());
```

## Emitted signals

**Traces**
- Outbox message dispatch spans (per message, tagged with message type and kind)
- Saga step spans (per saga orchestrator step, tagged with saga type and correlation ID)
- Consumer handler spans

**Metrics**
- `opinionatedeventing.outbox.pending` — current count of undelivered outbox messages
- `opinionatedeventing.outbox.processed` — count of successfully dispatched messages
- `opinionatedeventing.outbox.failed` — count of permanently failed messages
- `opinionatedeventing.publish.duration` — time to write a message to the outbox
- `opinionatedeventing.dispatch.duration` — time to deliver a message to the broker
- `opinionatedeventing.consume.duration` — time to process an inbound message
- `opinionatedeventing.saga.active` — current count of active saga instances
- `opinionatedeventing.saga.timed_out` — count of timed-out sagas

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)
