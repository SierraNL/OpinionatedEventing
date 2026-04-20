using Microsoft.EntityFrameworkCore;
using OpinionatedEventing;
using Samples.Contracts.Events;
using Samples.OrderService.Application;
using Samples.OrderService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ── Core messaging ────────────────────────────────────────────────────────────
builder.Services
    .AddOpinionatedEventing()
    .AddHandlersFromAssemblies(typeof(OrderSaga).Assembly)
    .AddOutbox();

// ── Infrastructure ────────────────────────────────────────────────────────────
builder.Services.AddOrderServiceInfrastructure(
    builder.Configuration.GetConnectionString("OrderDb") ?? "Data Source=orders.db");

// ── Saga engine ───────────────────────────────────────────────────────────────
builder.Services.AddOpinionatedEventingSagas();
builder.Services.AddSaga<OrderSaga>();

// Bridge: register IEventHandler<T> adapters so the RabbitMQ topology initializer
// creates subscriptions and the consumer routes these events to the saga dispatcher.
builder.Services.AddScoped<IEventHandler<OrderPlaced>, SagaEventHandlerAdapter<OrderPlaced>>();
builder.Services.AddScoped<IEventHandler<PaymentReceived>, SagaEventHandlerAdapter<PaymentReceived>>();
builder.Services.AddScoped<IEventHandler<PaymentFailed>, SagaEventHandlerAdapter<PaymentFailed>>();
builder.Services.AddScoped<IEventHandler<StockReserved>, SagaEventHandlerAdapter<StockReserved>>();

// ── Transport ─────────────────────────────────────────────────────────────────
// Reads ConnectionStrings__rabbitmq injected by Aspire; falls back to RabbitMQOptions.ConnectionString.
builder.Services.AddRabbitMQTransport(options =>
{
    options.ServiceName = "order-service";
    options.AutoDeclareTopology = true;
});

// ── Use cases ─────────────────────────────────────────────────────────────────
builder.Services.AddScoped<PlaceOrderUseCase>();

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddOpinionatedEventingHealthChecks();

var app = builder.Build();

// Ensure DB is created on first run (SQLite — no migration needed for a sample).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapPost("/orders", async (PlaceOrderRequest request, PlaceOrderUseCase useCase, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.CustomerName))
        return Results.Problem("CustomerName is required.", statusCode: 400);
    if (request.Total <= 0)
        return Results.Problem("Total must be greater than zero.", statusCode: 400);

    var orderId = await useCase.ExecuteAsync(request.CustomerName, request.Total, ct);
    return Results.Created($"/orders/{orderId}", new { orderId });
});

app.MapHealthChecks("/health");

app.Run();

public record PlaceOrderRequest(string CustomerName, decimal Total);
