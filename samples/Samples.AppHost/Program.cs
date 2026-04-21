using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var rabbit = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();

// PostgreSQL container with one logical database per service.
// SQLite was intentionally avoided: its DateTimeOffset handling breaks saga timeouts.
var postgres = builder.AddPostgres("postgres").WithPgAdmin();
var orderDb        = postgres.AddDatabase("orderdb");
var paymentDb      = postgres.AddDatabase("paymentdb");
var fulfillmentDb  = postgres.AddDatabase("fulfillmentdb");
var notificationDb = postgres.AddDatabase("notificationdb");

builder.AddProject<Projects.Samples_OrderService_Api>("order-service")
    .WithReference(rabbit).WaitFor(rabbit)
    .WithReference(orderDb).WaitFor(postgres);

builder.AddProject<Projects.Samples_PaymentService>("payment-service")
    .WithReference(rabbit).WaitFor(rabbit)
    .WithReference(paymentDb).WaitFor(postgres);

builder.AddProject<Projects.Samples_FulfillmentService>("fulfillment-service")
    .WithReference(rabbit).WaitFor(rabbit)
    .WithReference(fulfillmentDb).WaitFor(postgres);

builder.AddProject<Projects.Samples_NotificationService>("notification-service")
    .WithReference(rabbit).WaitFor(rabbit)
    .WithReference(notificationDb).WaitFor(postgres);

builder.Build().Run();
