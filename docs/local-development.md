# Local Development

OpinionatedEventing is designed to run locally without any cloud accounts. The `OpinionatedEventing.Aspire` package provides Aspire AppHost extensions that spin up RabbitMQ or the Azure Service Bus emulator as Docker containers, with zero configuration required.

## Prerequisites

- [.NET Aspire workload](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/setup-tooling): `dotnet workload install aspire`
- Docker Desktop (or compatible container runtime)

## Project structure

An Aspire solution typically has at least two projects:

```
YourSolution.AppHost/    ← Aspire host (orchestrates resources)
YourSolution.ApiService/ ← Your application service(s)
```

Add the Aspire package to the AppHost:

```
dotnet add package OpinionatedEventing.Aspire
```

## RabbitMQ

### AppHost setup

```csharp
// AppHost/Program.cs
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var rabbit = builder.AddRabbitMqMessaging("rabbitmq");

var api = builder.AddProject<Projects.YourSolution_ApiService>("api")
    .WithReference(rabbit);

builder.Build().Run();
```

`AddRabbitMqMessaging` starts a RabbitMQ container with the management plugin enabled. It injects a `ConnectionStrings__rabbitmq` environment variable into any project that references it.

### Service setup

In your service project, use `AddRabbitMQTransport` and leave `ConnectionString` unset — the transport auto-discovers it from `IConfiguration["ConnectionStrings:rabbitmq"]`:

```csharp
services.AddRabbitMQTransport(options =>
{
    options.ServiceName = "order-service";
    options.AutoDeclareTopology = true; // declare exchanges and queues at startup
});
```

## Azure Service Bus emulator

### AppHost setup

```csharp
// AppHost/Program.cs
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var asb = builder.AddAzureServiceBusEmulator("servicebus");

var api = builder.AddProject<Projects.YourSolution_ApiService>("api")
    .WithReference(asb);

builder.Build().Run();
```

`AddAzureServiceBusEmulator` starts the Azure Service Bus emulator container and injects its connection string.

### Service setup

```csharp
services.AddAzureServiceBusTransport(options =>
{
    options.ServiceName = "order-service";
    options.AutoCreateResources = true; // create topics and queues at startup
});
```

The transport reads the injected connection string automatically — no manual configuration needed.

## Switching transports

Switching between RabbitMQ and Azure Service Bus requires only a DI change. Handler, aggregate, and saga code is identical for both transports.

A common pattern is to use `IConfiguration` or environment variables to select the transport:

```csharp
if (builder.Configuration["Transport"] == "AzureServiceBus")
{
    services.AddAzureServiceBusTransport(options =>
    {
        options.ServiceName = "order-service";
        options.AutoCreateResources = true;
    });
}
else
{
    services.AddRabbitMQTransport(options =>
    {
        options.ServiceName = "order-service";
        options.AutoDeclareTopology = true;
    });
}
```

In `appsettings.Development.json`:

```json
{
  "Transport": "RabbitMQ"
}
```

## Running locally

```bash
cd src/YourSolution.AppHost
dotnet run
```

The Aspire dashboard opens automatically at `http://localhost:15888`. It shows:

- All running services and their health status
- Console output per service
- Distributed traces (if OTel is configured)
- Resource connection strings

## Health checks

Both transport packages expose health checks via the `OpinionatedEventing.Aspire` extensions:

```csharp
services.AddHealthChecks()
    .AddOpinionatedEventingHealthChecks(options =>
    {
        options.OutboxBacklogThreshold = 100;
        options.SagaTimeoutBacklogThreshold = 10;
    });

app.MapHealthChecks("/health");
```

The Aspire dashboard polls `/health` and reflects the status in the resources view.

| Health check | Tag | Condition |
|---|---|---|
| Broker connectivity | `live`, `broker` | Unhealthy if broker is unreachable |
| Outbox backlog | `ready`, `outbox` | Degraded above `OutboxBacklogThreshold` |
| Saga timeout backlog | `ready`, `saga` | Degraded above `SagaTimeoutBacklogThreshold` |

## RabbitMQ management UI

When using `AddRabbitMqMessaging`, the management plugin is enabled. Access the RabbitMQ management UI at:

```
http://localhost:15672
```

Default credentials: `guest` / `guest`

Here you can inspect exchanges, queues, bindings, and message rates in real time.
