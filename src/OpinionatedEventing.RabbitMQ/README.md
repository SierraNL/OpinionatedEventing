# OpinionatedEventing.RabbitMQ

RabbitMQ transport for OpinionatedEventing.

- **Events** (`IEvent`) → topic **exchanges** with per-handler bindings
- **Commands** (`ICommand`) → direct **queues** (single handler)
- Automatically declares exchanges, queues, and bindings on startup
- Supports .NET Aspire service discovery

## Installation

```
dotnet add package OpinionatedEventing.RabbitMQ
```

## Registration

```csharp
builder.Services
    .AddOpinionatedEventing()
    .AddOutbox();

builder.Services.AddRabbitMQTransport(options =>
{
    options.HostName = "localhost";
    options.Port = 5672;
    options.UserName = "guest";
    options.Password = "guest";
    options.VirtualHost = "/";
});
```

For local development with a containerised RabbitMQ, use `OpinionatedEventing.Aspire` — it wires up service discovery automatically so no manual hostname configuration is needed.

## Handler registration

Handlers registered via `AddOpinionatedEventing()` are automatically wired to the correct exchange binding / queue. No per-handler configuration required.

```csharp
builder.Services.AddOpinionatedEventing()
    .AddHandlersFromAssemblies(typeof(Program).Assembly);
```

## Documentation

Guides, API cheat sheet and recipes: [opinionated-eventing.com](https://opinionated-eventing.com/). AI coding agents: start at [llms.txt](https://opinionated-eventing.com/llms.txt) and the [agent guide](https://opinionated-eventing.com/docs/ai/agent-guide).

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)
