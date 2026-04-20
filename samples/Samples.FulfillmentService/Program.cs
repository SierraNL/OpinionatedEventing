using Microsoft.EntityFrameworkCore;
using Samples.Contracts.Events;
using Samples.FulfillmentService;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOpinionatedEventing()
    .AddHandlersFromAssemblies(typeof(FulfillmentParticipant).Assembly)
    .AddOutbox();

// Registers IOutboxStore and ISagaStateStore (required by ISagaDispatcher).
// DomainEventInterceptor is also registered but not wired — this service has no aggregates.
builder.Services.AddOpinionatedEventingEntityFramework<FulfillmentDbContext>();
builder.Services.AddDbContext<FulfillmentDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("FulfillmentDb") ?? "Data Source=fulfillment.db"));

// Saga engine (participant-only — no orchestrators registered here).
builder.Services.AddOpinionatedEventingSagas();
builder.Services.AddSagaParticipant<FulfillmentParticipant>();

// Bridge: PaymentReceived must arrive as IEventHandler<PaymentReceived>
// so the topology initializer creates the queue binding.
builder.Services.AddScoped<IEventHandler<PaymentReceived>, SagaEventHandlerAdapter<PaymentReceived>>();

builder.Services.AddRabbitMQTransport(options =>
{
    options.ServiceName = "fulfillment-service";
    options.AutoDeclareTopology = true;
});

builder.Services.AddHealthChecks()
    .AddOpinionatedEventingHealthChecks();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapHealthChecks("/health");

app.Run();
