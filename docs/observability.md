# Observability

OpinionatedEventing emits structured logs, distributed traces, and metrics out of the box. All telemetry uses standard .NET abstractions — no Serilog, Seq, or OpenTelemetry package is required in the library itself.

## Logging

The library uses `Microsoft.Extensions.Logging.ILogger<T>`. As long as you configure a logging provider in your host, log output flows automatically.

### What is logged

| Component | Level | Events |
|---|---|---|
| `OutboxDispatcherWorker` | `Debug` | Poll cycle start/end, batch size |
| `OutboxDispatcherWorker` | `Information` | Message dispatched successfully |
| `OutboxDispatcherWorker` | `Warning` | Dispatch attempt failed (retrying) |
| `OutboxDispatcherWorker` | `Error` | Message dead-lettered after MaxAttempts |
| `SagaTimeoutWorker` | `Debug` | Timeout check cycle |
| `SagaTimeoutWorker` | `Information` | Saga timed out, timeout handler invoked |
| `SagaTimeoutWorker` | `Error` | Timeout handler threw exception |
| Transport consumers | `Debug` | Message received |
| Transport consumers | `Information` | Message handled successfully |
| Transport consumers | `Error` | Handler threw unhandled exception |

### Configuring log levels

In `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "OpinionatedEventing": "Debug"
    }
  }
}
```

## Distributed tracing

The library uses `System.Diagnostics.ActivitySource`. All activity source names are under the `opinionatedeventing` prefix. Wire them up via the OpenTelemetry SDK:

```csharp
using OpinionatedEventing.OpenTelemetry;

services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddOpinionatedEventingInstrumentation()
        .AddOtlpExporter());
```

### Emitted spans

| Span name | When | Key attributes |
|---|---|---|
| `outbox.publish` | `IPublisher` writes to the outbox store | `message.type`, `message.kind`, `correlation.id` |
| `outbox.dispatch` | `OutboxDispatcherWorker` calls `ITransport.SendAsync` | `message.type`, `message.id`, `attempt.count` |
| `message.consume` | Transport consumer hands off to handler(s) | `message.type`, `handler.type`, `correlation.id`, `causation.id` |
| `saga.step` | A saga handler executes | `saga.type`, `saga.status`, `correlation.id` |

Each span carries W3C trace context propagated through the message envelope so traces span service boundaries.

### Viewing traces locally

When using Aspire, the dashboard includes a built-in trace viewer. Traces from all services are aggregated automatically — no additional exporter configuration is needed for local development.

## Metrics

The library uses `System.Diagnostics.Metrics.Meter`. Wire up the metrics provider:

```csharp
using OpinionatedEventing.OpenTelemetry;

services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddOpinionatedEventingInstrumentation()
        .AddOtlpExporter());
```

### Available instruments

| Instrument | Type | Description |
|---|---|---|
| `opinionatedeventing.outbox.pending` | Gauge | Current number of pending (unprocessed) outbox messages |
| `opinionatedeventing.outbox.processed` | Counter | Total messages successfully dispatched to the broker |
| `opinionatedeventing.outbox.failed` | Counter | Total messages dead-lettered after MaxAttempts |
| `opinionatedeventing.publish.duration` | Histogram | Time (ms) to write a message to the outbox store |
| `opinionatedeventing.dispatch.duration` | Histogram | Time (ms) to dispatch a message to the broker |
| `opinionatedeventing.consume.duration` | Histogram | Time (ms) for the handler to process a message |
| `opinionatedeventing.saga.active` | Gauge | Current number of active saga instances |
| `opinionatedeventing.saga.timed_out` | Counter | Total sagas that have timed out |

### Alerting recommendations

| Metric | Alert condition | Meaning |
|---|---|---|
| `opinionatedeventing.outbox.pending` | > threshold for > 5 min | Dispatcher is stalled or the broker is unavailable |
| `opinionatedeventing.outbox.failed` | Rate > 0 | Messages are dead-lettering — investigate errors |
| `opinionatedeventing.consume.duration` | p99 > SLA | Handler is slow — risk of broker timeout and redelivery |
| `opinionatedeventing.saga.active` | Growing without bound | Sagas are not completing — check timeouts |

## Health checks

Health checks are the quickest way to surface observability in an operations dashboard:

```csharp
services.AddHealthChecks()
    .AddOpinionatedEventingHealthChecks(options =>
    {
        options.OutboxBacklogThreshold = 100;    // pending messages before Degraded
        options.SagaTimeoutBacklogThreshold = 10; // expired sagas before Degraded
    });

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live",  new HealthCheckOptions { Predicate = c => c.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
```

All three built-in checks are tagged `ready`. Broker connectivity belongs on readiness, not liveness: a broker outage is a dependency failure that restarting the process cannot fix — failing liveness would just cause needless container restarts.

**Important limitation for event-driven services:** readiness probes only control the Kubernetes `Service` endpoint, which governs *HTTP* traffic. The broker consumer workers (`RabbitMQConsumerWorker`, `AzureServiceBusConsumerWorker`) are `BackgroundService` instances that pull messages directly from the broker, independent of any load balancer. Failing readiness does **not** pause event consumption.

This means the health checks here are best treated as **observability signals** — surfacing problems in dashboards and triggering alerts — rather than as back-pressure mechanisms. If you need to actually stop a service from consuming events (for example, during an overload situation), you need an explicit mechanism such as scaling down the deployment or pausing the consumer workers in application code.

## Correlation and causation IDs

Every message carries two identifiers that let you reconstruct the full causal chain across services:

- **CorrelationId** — Set at the entry point (e.g., an HTTP request ID) and propagated through every event and command in the chain. All spans and log entries include this.
- **CausationId** — The `Id` of the message that caused this one to be sent. Null for root messages. This is how you reconstruct the parent-child relationship between messages.

These IDs are accessible in handlers via `IMessagingContext`:

```csharp
public class OrderNotificationHandler(IMessagingContext context) : IEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced @event, CancellationToken ct)
    {
        // context.CorrelationId — same across the entire order flow
        // context.CausationId  — ID of the message that triggered this handler
        return Task.CompletedTask;
    }
}
```

### Automatic logging scope

`MessageHandlerRunner` calls `ILogger.BeginScope` before invoking handlers, pushing `CorrelationId`, `CausationId`, and `MessageType` as structured properties into the ambient logging scope:

```json
{
  "CorrelationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "CausationId":   "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "MessageType":   "MyApp.Orders.OrderPlaced, MyApp"
}
```

Any `ILogger<T>` used inside a handler automatically inherits these properties — no manual logging required. Logging providers that support structured scopes (OpenTelemetry, Serilog, Application Insights) pick them up and include them on every log entry emitted during handler execution.
