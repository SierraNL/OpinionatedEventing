# OpinionatedEventing.Testing

Test helpers for OpinionatedEventing. Provides in-memory fakes and a fluent builder for unit testing handlers, sagas, and aggregates without any infrastructure.

**Not for production use.**

## Installation

```
dotnet add package OpinionatedEventing.Testing
```

## What's included

| Type | Purpose |
|---|---|
| `InMemoryOutboxStore` | `IOutboxStore` backed by a `List<OutboxMessage>` |
| `FakePublisher` | `IPublisher` that captures sent commands and published events |
| `TestMessagingBuilder` | Fluent builder that wires up a full in-memory DI container |

## Usage

### FakePublisher

```csharp
var publisher = new FakePublisher();
var handler = new PlaceOrderHandler(publisher);

await handler.HandleAsync(new PlaceOrder(orderId), CancellationToken.None);

Assert.Single(publisher.PublishedEvents.OfType<OrderPlaced>());
```

### TestMessagingBuilder

```csharp
var host = new TestMessagingBuilder()
    .WithHandler<OrderPlacedHandler>()
    .Build();

var publisher = host.Services.GetRequiredService<IPublisher>();
await publisher.PublishEventAsync(new OrderPlaced(Guid.NewGuid()));

// assert side effects via your fake dependencies
```

### InMemoryOutboxStore

```csharp
var store = new InMemoryOutboxStore();
// inject into the class under test
// inspect store.Messages after the act
```

## Repository

[github.com/SierraNL/OpinionatedEventing](https://github.com/SierraNL/OpinionatedEventing)
