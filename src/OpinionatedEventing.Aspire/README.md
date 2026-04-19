# OpinionatedEventing.Aspire

.NET Aspire AppHost extensions for OpinionatedEventing local development. Adds first-class support for:

- **RabbitMQ** container resource with auto-wired service discovery
- **Azure Service Bus emulator** (Docker) resource

Switch between transports in DI only — no handler code changes needed.

## Installation

In your **AppHost** project:

```
dotnet add package OpinionatedEventing.Aspire
```

## RabbitMQ

```csharp
// AppHost/Program.cs
var rabbit = builder.AddRabbitMqMessaging("messaging");

var api = builder.AddProject<Projects.MyApi>("api")
    .WithReference(rabbit);
```

In your **API** project, `AddRabbitMQTransport` picks up the connection from Aspire service discovery automatically — no `HostName` or port configuration needed.

## Azure Service Bus emulator

```csharp
// AppHost/Program.cs
var asb = builder.AddAzureServiceBusEmulator("messaging");

var api = builder.AddProject<Projects.MyApi>("api")
    .WithReference(asb);
```

## Health checks

```csharp
// API project
builder.Services.AddHealthChecks()
    .AddOpinionatedEventingHealthChecks();
```

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)
