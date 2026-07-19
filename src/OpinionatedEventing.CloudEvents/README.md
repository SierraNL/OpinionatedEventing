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

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)
