# OpinionatedEventing.CloudEvents.RabbitMQ

Opt-in [CloudEvents 1.0](https://cloudevents.io/) structured envelope for the RabbitMQ transport. Services that don't need it are completely unaffected — commands always stay in the broker-native format, and events do too unless you opt in.

## Installation

```
dotnet add package OpinionatedEventing.CloudEvents.RabbitMQ
```

## Registration

```csharp
services.AddRabbitMQTransport(opts => ...)
        .UseCloudEventsEnvelope(opts => opts.Source = new Uri("urn:order-service"));
```

Once registered, every outbound **event** is serialised as a CloudEvents structured envelope (`application/cloudevents+json`) instead of the broker-native header-based format. Commands are never wrapped. Inbound messages are detected by content type, so a consumer with `UseCloudEventsEnvelope()` enabled can still receive broker-native commands and events from services that haven't opted in.

See [OpinionatedEventing.CloudEvents](https://www.nuget.org/packages/OpinionatedEventing.CloudEvents) for the attribute mapping and `CloudEventsOptions` reference.

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)
