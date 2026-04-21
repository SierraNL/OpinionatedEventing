using Microsoft.EntityFrameworkCore;
using Samples.NotificationService;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOpinionatedEventing()
    .AddHandlersFromAssemblies(typeof(OrderPlacedHandler).Assembly)
    .AddOutbox();

// Registers IOutboxStore. DomainEventInterceptor is also registered but not wired
// — this service has no aggregates and never publishes outbound messages.
builder.Services.AddOpinionatedEventingEntityFramework<NotificationDbContext>();
builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("notificationdb")
            ?? throw new InvalidOperationException("Connection string 'notificationdb' not found.")));

builder.Services.AddRabbitMQTransport(options =>
{
    options.ServiceName = "notification-service";
    options.AutoDeclareTopology = true;
});

builder.Services.AddHealthChecks()
    .AddOpinionatedEventingHealthChecks();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapHealthChecks("/health");

app.Run();
