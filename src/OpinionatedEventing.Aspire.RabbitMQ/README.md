# OpinionatedEventing.Aspire.RabbitMQ

Aspire AppHost extension for OpinionatedEventing — adds a RabbitMQ container resource with the management plugin enabled. Reference this package from your AppHost project only.

## Installation

```
dotnet add package OpinionatedEventing.Aspire.RabbitMQ
```

## Usage

```csharp
// AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var rabbit = builder.AddRabbitMqMessaging("messaging");

builder.AddProject<Projects.OrderService>("order-service")
       .WithReference(rabbit)
       .WaitFor(rabbit);
```

The referenced service reads the connection string automatically via `OpinionatedEventing.RabbitMQ`.

## Documentation

Guides, API cheat sheet and recipes: [opinionated-eventing.com](https://opinionated-eventing.com/). AI coding agents: start at [llms.txt](https://opinionated-eventing.com/llms.txt) and the [agent guide](https://opinionated-eventing.com/docs/ai/agent-guide).

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)