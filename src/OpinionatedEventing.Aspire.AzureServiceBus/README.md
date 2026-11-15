# OpinionatedEventing.Aspire.AzureServiceBus

Aspire AppHost extension for OpinionatedEventing — adds an Azure Service Bus emulator resource. Reference this package from your AppHost project only.

## Installation

```
dotnet add package OpinionatedEventing.Aspire.AzureServiceBus
```

## Usage

```csharp
// AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var bus = builder.AddAzureServiceBusEmulator("messaging");

builder.AddProject<Projects.OrderService>("order-service")
       .WithReference(bus)
       .WaitFor(bus);
```

The referenced service reads the emulator connection string automatically via `OpinionatedEventing.AzureServiceBus`.

## Documentation

Guides, API cheat sheet and recipes: [opinionated-eventing.com](https://opinionated-eventing.com/). AI coding agents: start at [llms.txt](https://opinionated-eventing.com/llms.txt) and the [agent guide](https://opinionated-eventing.com/docs/ai/agent-guide).

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)