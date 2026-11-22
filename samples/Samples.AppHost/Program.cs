using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var rabbit = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();

// PostgreSQL container with one logical database per service.
// SQLite was intentionally avoided: its DateTimeOffset handling breaks saga timeouts.
var postgres = builder.AddPostgres("postgres").WithPgAdmin();
var orderDb        = postgres.AddDatabase("orderdb");
var paymentDb      = postgres.AddDatabase("paymentdb");
var fulfillmentDb  = postgres.AddDatabase("fulfillmentdb");
builder.AddDotnetProject("order-service", "../Samples.OrderService.Api/Samples.OrderService.Api.csproj")
    .WithReference(rabbit).WaitFor(rabbit)
    .WithReference(orderDb).WaitFor(postgres);

builder.AddDotnetProject("payment-service", "../Samples.PaymentService/Samples.PaymentService.csproj")
    .WithReference(rabbit).WaitFor(rabbit)
    .WithReference(paymentDb).WaitFor(postgres);

builder.AddDotnetProject("fulfillment-service", "../Samples.FulfillmentService/Samples.FulfillmentService.csproj")
    .WithReference(rabbit).WaitFor(rabbit)
    .WithReference(fulfillmentDb).WaitFor(postgres);

builder.AddDotnetProject("notification-service", "../Samples.NotificationService/Samples.NotificationService.csproj")
    .WithReference(rabbit).WaitFor(rabbit);

builder.Build().Run();
