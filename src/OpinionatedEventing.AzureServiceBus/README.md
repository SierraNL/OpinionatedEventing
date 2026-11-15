# OpinionatedEventing.AzureServiceBus

Azure Service Bus transport for OpinionatedEventing.

- **Events** (`IEvent`) → Service Bus **topics** with per-handler subscriptions
- **Commands** (`ICommand`) → Service Bus **queues** (single handler)
- Uses `DefaultAzureCredential` by default — no connection string required in production
- Automatically creates topics, queues, and subscriptions on startup

## Installation

```
dotnet add package OpinionatedEventing.AzureServiceBus
```

## Registration

```csharp
builder.Services
    .AddOpinionatedEventing()
    .AddOutbox();

builder.Services.AddAzureServiceBusTransport(options =>
{
    options.FullyQualifiedNamespace = "my-namespace.servicebus.windows.net";
});
```

For local development with the Azure Service Bus emulator, use `OpinionatedEventing.Aspire` instead of configuring the namespace manually.

## Handler registration

Handlers registered via `AddOpinionatedEventing()` are automatically wired to the correct subscription/queue. No additional configuration per handler is needed.

```csharp
builder.Services.AddOpinionatedEventing()
    .AddHandlersFromAssemblies(typeof(Program).Assembly);
```

## Authentication

In production, assign the **Azure Service Bus Data Owner** (or Data Sender + Data Receiver) role to your managed identity. Locally, `az login` is sufficient when using `DefaultAzureCredential`.

## Documentation

Guides, API cheat sheet and recipes: [opinionated-eventing.com](https://opinionated-eventing.com/). AI coding agents: start at [llms.txt](https://opinionated-eventing.com/llms.txt) and the [agent guide](https://opinionated-eventing.com/docs/ai/agent-guide).

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)
