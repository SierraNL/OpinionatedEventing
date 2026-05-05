# OpinionatedEventing — Sample

A runnable end-to-end demo of the library: four .NET services communicating over RabbitMQ, with PostgreSQL for persistence and saga state, all orchestrated by .NET Aspire.

## Architecture

```
POST /orders
     │
     ▼
OrderService ──── ProcessPayment (cmd) ───► PaymentService
(orchestrator) ◄── PaymentReceived (event) ──┤
     │                                        │ PaymentReceived (fan-out)
     │ observes                               ▼
     │ StockReserved            FulfillmentService (saga participant)
     │◄── StockReserved (event) ─────────────┘
     │
     │                    NotificationService subscribes to:
     └──────────────────► OrderPlaced · PaymentReceived · StockReserved
```

| Service | Role | Persistence |
|---|---|---|
| `order-service` | Saga orchestrator — drives the order workflow | PostgreSQL (`orderdb`) |
| `payment-service` | Handles `ProcessPayment` commands, publishes `PaymentReceived`/`PaymentFailed` | PostgreSQL (`paymentdb`) |
| `fulfillment-service` | Saga participant — reserves stock, publishes `StockReserved` | PostgreSQL (`fulfillmentdb`) |
| `notification-service` | Subscribes to all outcome events, logs notifications | none |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A container runtime: Docker Desktop, Podman, or Rancher Desktop

## Running

From the repository root:

```bash
# With the Aspire CLI (winget install Microsoft.Aspire)
aspire run

# Or with dotnet directly
dotnet run --project samples/Samples.AppHost
```

The Aspire dashboard URL is printed to the console at startup. It shows all services, logs, and distributed traces.

## Placing an order

Once all four services show **Running** and healthy in the dashboard, find the `order-service` endpoint URL (shown under its resource in the dashboard) and POST an order:

```bash
curl -X POST http://localhost:<order-service-port>/orders \
  -H "Content-Type: application/json" \
  -d '{"customerName": "Alice", "total": 99.99}'
```

```json
{ "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6" }
```

Watch the dashboard logs to see the saga progress:

1. `order-service` publishes `OrderPlaced` → `OrderSaga` starts, sends `ProcessPayment` command to payment-service
2. `payment-service` handles `ProcessPayment` → publishes `PaymentReceived`
3. `fulfillment-service` reacts to `PaymentReceived` (saga participant) → publishes `StockReserved`
4. `OrderSaga` observes `StockReserved` → order complete
5. `notification-service` logs a notification for `OrderPlaced`, `PaymentReceived`, and `StockReserved`

## RabbitMQ management UI

The RabbitMQ management plugin is enabled. Open [http://localhost:15672](http://localhost:15672) (credentials: `guest` / `guest`) to inspect exchanges, queues, bindings, and message rates live.
