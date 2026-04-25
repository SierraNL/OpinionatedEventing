using Samples.NotificationService;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOpinionatedEventing()
    .AddHandlersFromAssemblies(typeof(OrderPlacedHandler).Assembly);

builder.Services.AddRabbitMQTransport(options =>
{
    options.ServiceName = "notification-service";
    options.AutoDeclareTopology = true;
});

builder.Services.AddHealthChecks()
    .AddOpinionatedEventingHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
