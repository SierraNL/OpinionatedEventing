# OpinionatedEventing.CloudEvents

Core [CloudEvents 1.0](https://cloudevents.io/) structured-envelope mapping for OpinionatedEventing. Maps `OutboxMessage` to and from the CloudEvents JSON envelope with no broker dependency.

This package on its own does nothing — pair it with a transport-specific package:

- `OpinionatedEventing.CloudEvents.AzureServiceBus`
- `OpinionatedEventing.CloudEvents.RabbitMQ`

## Attribute mapping

Only **events** are wrapped; commands remain in the broker-native format.

| CloudEvents | Source |
|---|---|
| `id` | `OutboxMessage.Id` |
| `type` | `OutboxMessage.MessageType`, or `CloudEventsOptions.TypeFormatter(message)` if configured |
| `source` | `CloudEventsOptions.Source` |
| `specversion` | `"1.0"` |
| `datacontenttype` | `"application/json"` |
| `time` | `OutboxMessage.CreatedAt` |
| `data` | `OutboxMessage.Payload` (raw JSON) |
| `correlationid` *(extension)* | `OutboxMessage.CorrelationId` |
| `causationid` *(extension)* | `OutboxMessage.CausationId` (omitted if null) |

Content mode: **structured** (`application/cloudevents+json`). Binary content mode is out of scope.

## Usage

This package is a dependency of the transport-specific packages; you normally don't register anything from it directly. Opt in on your transport instead:

```csharp
services.AddRabbitMQTransport(o => o.ServiceName = "order-service")
        .UseCloudEventsEnvelope(o => o.Source = new Uri("urn:order-service")); // or AzureServiceBus variant
```

## Documentation

Guides, API cheat sheet and recipes: [opinionated-eventing.com](https://opinionated-eventing.com/). AI coding agents: start at [llms.txt](https://opinionated-eventing.com/llms.txt) and the [agent guide](https://opinionated-eventing.com/docs/ai/agent-guide).

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)
