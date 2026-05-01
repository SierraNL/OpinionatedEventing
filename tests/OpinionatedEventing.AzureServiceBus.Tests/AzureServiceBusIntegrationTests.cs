#nullable enable

using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpinionatedEventing.AzureServiceBus.Attributes;
using OpinionatedEventing.AzureServiceBus.Tests.TestSupport;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.AzureServiceBus.Tests;

/// <summary>
/// Integration tests for the Azure Service Bus transport.
/// Require Docker with the Azure Service Bus Emulator image available.
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
[Collection(AzureServiceBusCollection.Name)]
public sealed class AzureServiceBusIntegrationTests
{
    private readonly AzureServiceBusFixture _fixture;

    /// <summary>Initialises the test class with the shared Azure Service Bus emulator fixture.</summary>
    public AzureServiceBusIntegrationTests(AzureServiceBusFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Published_event_is_consumed_by_handler()
    {
        var ct = TestContext.Current.CancellationToken;
        var received = new List<OrderPlaced>();

        using var host = BuildHost(services =>
        {
            services.AddScoped<IEventHandler<OrderPlaced>>(_ =>
                new CapturingEventHandler<OrderPlaced>(received));
        });

        await host.StartAsync(ct);
        await Task.Delay(500, ct);

        var transport = host.Services.GetRequiredService<ITransport>();
        await transport.SendAsync(BuildOutboxMessage(new OrderPlaced("order-1"), "Event"), ct);

        await WaitForConditionAsync(() => received.Count == 1, ct);

        Assert.Single(received);
        Assert.Equal("order-1", received[0].OrderId);

        await host.StopAsync(ct);
    }

    [Fact]
    public async Task Sent_command_is_consumed_by_single_handler()
    {
        var ct = TestContext.Current.CancellationToken;
        var received = new List<ProcessPayment>();

        using var host = BuildHost(services =>
        {
            services.AddScoped<ICommandHandler<ProcessPayment>>(_ =>
                new CapturingCommandHandler<ProcessPayment>(received));
        });

        await host.StartAsync(ct);
        await Task.Delay(500, ct);

        var transport = host.Services.GetRequiredService<ITransport>();
        await transport.SendAsync(BuildOutboxMessage(new ProcessPayment("payment-1", 99m), "Command"), ct);

        await WaitForConditionAsync(() => received.Count == 1, ct);

        Assert.Single(received);
        Assert.Equal("payment-1", received[0].PaymentId);

        await host.StopAsync(ct);
    }

    [Fact]
    public async Task Session_enabled_command_is_consumed_via_session_processor()
    {
        var ct = TestContext.Current.CancellationToken;
        var received = new List<CheckoutOrder>();

        using var host = BuildHost(
            services =>
            {
                services.AddScoped<ICommandHandler<CheckoutOrder>>(_ =>
                    new CapturingCommandHandler<CheckoutOrder>(received));
            },
            enableSessions: true);

        await host.StartAsync(ct);
        await Task.Delay(500, ct);

        var transport = host.Services.GetRequiredService<ITransport>();
        await transport.SendAsync(BuildOutboxMessage(new CheckoutOrder("checkout-1"), "Command"), ct);

        await WaitForConditionAsync(() => received.Count == 1, ct);

        Assert.Single(received);
        Assert.Equal("checkout-1", received[0].CheckoutId);

        await host.StopAsync(ct);
    }

    [Fact]
    public async Task Message_without_required_properties_is_dead_lettered_without_invoking_handler()
    {
        var ct = TestContext.Current.CancellationToken;
        var received = new List<OrderPlaced>();

        using var host = BuildHost(services =>
        {
            services.AddScoped<IEventHandler<OrderPlaced>>(_ =>
                new CapturingEventHandler<OrderPlaced>(received));
        });

        await host.StartAsync(ct);
        await Task.Delay(500, ct);

        // Send a raw message with no OpinionatedEventing application properties.
        var client = host.Services.GetRequiredService<ServiceBusClient>();
        await using var sender = client.CreateSender("order-placed");
        await sender.SendMessageAsync(new ServiceBusMessage("{\"OrderId\":\"raw\"}"), ct);

        // Allow time for the message to be processed; handler must NOT be invoked.
        await Task.Delay(2_000, ct);
        Assert.Empty(received);

        await host.StopAsync(ct);
    }

    [Fact]
    public async Task Topology_initializer_is_idempotent_when_resources_already_exist()
    {
        var ct = TestContext.Current.CancellationToken;

        // First host: creates topics, subscriptions, and queues.
        using (var first = BuildHost(services =>
        {
            services.AddScoped<IEventHandler<OrderPlaced>>(_ =>
                new CapturingEventHandler<OrderPlaced>(new List<OrderPlaced>()));
            services.AddScoped<ICommandHandler<ProcessPayment>>(_ =>
                new CapturingCommandHandler<ProcessPayment>(new List<ProcessPayment>()));
        }))
        {
            await first.StartAsync(ct);
            await first.StopAsync(ct);
        }

        // Second host: same handlers — topology initializer must hit "already exists" branches
        // on every resource without throwing.
        using var second = BuildHost(services =>
        {
            services.AddScoped<IEventHandler<OrderPlaced>>(_ =>
                new CapturingEventHandler<OrderPlaced>(new List<OrderPlaced>()));
            services.AddScoped<ICommandHandler<ProcessPayment>>(_ =>
                new CapturingCommandHandler<ProcessPayment>(new List<ProcessPayment>()));
        });

        var ex = await Record.ExceptionAsync(() => second.StartAsync(ct));
        Assert.Null(ex);
        await second.StopAsync(ct);
    }

    // --- helpers ---

    private IHost BuildHost(
        Action<IServiceCollection>? configureExtra = null,
        int maxDeliveryCount = 5,
        bool enableSessions = false)
        => Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddOpinionatedEventing();
                // Register the admin client before AddAzureServiceBusTransport so the TryAddSingleton
                // inside skips it. The management API is on port 5300; with UseDevelopmentEmulator=true
                // the SDK derives the HTTP endpoint from the port in the connection string, so using
                // the AMQP connection string (port 5672) would send HTTP to the AMQP listener.
                services.AddSingleton(
                    new ServiceBusAdministrationClient(_fixture.ManagementConnectionString));
                services.AddAzureServiceBusTransport(o =>
                {
                    o.ConnectionString = _fixture.ConnectionString;
                    o.ServiceName = "test-service";
                    o.AutoCreateResources = true;
                    o.MaxDeliveryCount = maxDeliveryCount;
                    o.EnableSessions = enableSessions;
                });
                configureExtra?.Invoke(services);
            })
            .Build();

    private static OutboxMessage BuildOutboxMessage<T>(T payload, string kind) where T : notnull
        => new()
        {
            Id = Guid.NewGuid(),
            MessageType = typeof(T).FullName!,
            MessageKind = kind,
            Payload = System.Text.Json.JsonSerializer.Serialize(payload),
            CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static async Task WaitForConditionAsync(
        Func<bool> condition, CancellationToken ct, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(100, ct);
        Assert.True(condition(), "Condition was not met within the timeout.");
    }

    // --- test message types ---

    private sealed record OrderPlaced(string OrderId) : IEvent;
    private sealed record ProcessPayment(string PaymentId, decimal Amount) : ICommand;

    [SessionEnabled]
    private sealed record CheckoutOrder(string CheckoutId) : ICommand;

    // --- handler implementations ---

    private sealed class CapturingEventHandler<T> : IEventHandler<T> where T : class, IEvent
    {
        private readonly List<T> _captured;
        public CapturingEventHandler(List<T> captured) => _captured = captured;
        public Task HandleAsync(T @event, CancellationToken ct) { _captured.Add(@event); return Task.CompletedTask; }
    }

    private sealed class CapturingCommandHandler<T> : ICommandHandler<T> where T : class, ICommand
    {
        private readonly List<T> _captured;
        public CapturingCommandHandler(List<T> captured) => _captured = captured;
        public Task HandleAsync(T command, CancellationToken ct) { _captured.Add(command); return Task.CompletedTask; }
    }

}
