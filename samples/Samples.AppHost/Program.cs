using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// RabbitMQ container (fanout exchanges + direct queues, management UI at :15672).
// WithManagementPlugin() enables the management UI on port 15672.
// To switch to Azure Service Bus emulator replace this line with:
//   var rabbit = builder.AddAzureServiceBus("servicebus").RunAsEmulator();
// and update each service's Program.cs to use AddAzureServiceBusTransport.
var rabbit = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();

// Each service receives ConnectionStrings__rabbitmq via Aspire service discovery.
// The OpinionatedEventing RabbitMQ transport reads this key automatically.
builder.AddProject<Projects.Samples_OrderService_Api>("order-service")
    .WithReference(rabbit)
    .WaitFor(rabbit);

builder.AddProject<Projects.Samples_PaymentService>("payment-service")
    .WithReference(rabbit)
    .WaitFor(rabbit);

builder.AddProject<Projects.Samples_FulfillmentService>("fulfillment-service")
    .WithReference(rabbit)
    .WaitFor(rabbit);

builder.AddProject<Projects.Samples_NotificationService>("notification-service")
    .WithReference(rabbit)
    .WaitFor(rabbit);

builder.Build().Run();
