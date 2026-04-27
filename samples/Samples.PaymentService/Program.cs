using Microsoft.EntityFrameworkCore;
using Samples.PaymentService;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOpinionatedEventing()
    .AddHandlersFromAssemblies(typeof(ProcessPaymentHandler).Assembly)
    .AddOutbox();

// Registers IOutboxStore and ISagaStateStore. DomainEventInterceptor is also registered
// but not wired into PaymentDbContext — this service has no aggregates.
builder.Services.AddOpinionatedEventingEntityFramework<PaymentDbContext>();
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("paymentdb")
            ?? throw new InvalidOperationException("Connection string 'paymentdb' not found.")));

builder.Services.AddRabbitMQTransport(options =>
{
    options.ServiceName = "payment-service";
    options.AutoDeclareTopology = true;
});

builder.Services.AddHealthChecks()
    .AddRabbitMqConnectivityHealthCheck()
    .AddOutboxBacklogHealthCheck();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapHealthChecks("/health");

app.Run();
