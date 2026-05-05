#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.RabbitMQ.Tests.TestSupport;
using OpinionatedEventing.Testing;
using RabbitMqClient = RabbitMQ.Client;
using Xunit;

namespace OpinionatedEventing.RabbitMQ.Tests;

/// <summary>
/// Integration tests for the RabbitMQ transport.
/// Require Docker with the RabbitMQ image available.
/// Run with: dotnet test --project tests/OpinionatedEventing.RabbitMQ.Tests/...
/// </summary>
[Trait("Category", "Integration")]
[Collection(RabbitMqCollection.Name)]
public sealed class RabbitMQIntegrationTests
{
    private readonly RabbitMqFixture _fixture;

    // Unique per instance (xUnit creates one per test) → isolated durable queues, no cross-test contamination.
    private readonly string _testRunId = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Initialises the test class with the shared RabbitMQ fixture.</summary>
    public RabbitMQIntegrationTests(RabbitMqFixture fixture)
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
        await transport.SendAsync(BuildOutboxMessage(new OrderPlaced("order-1"), MessageKind.Event), ct);

        await WaitForConditionAsync(() => received.Count == 1, ct);

        Assert.Single(received);
        Assert.Equal("order-1", received[0].OrderId);

        await host.StopAsync(ct);
    }

    [Fact]
    public async Task Published_event_is_consumed_by_multiple_handlers()
    {
        var ct = TestContext.Current.CancellationToken;
        var receivedA = new List<OrderPlaced>();
        var receivedB = new List<OrderPlaced>();

        using var hostA = BuildHost(
            services => services.AddScoped<IEventHandler<OrderPlaced>>(
                _ => new CapturingEventHandler<OrderPlaced>(receivedA)),
            serviceName: "service-a");

        using var hostB = BuildHost(
            services => services.AddScoped<IEventHandler<OrderPlaced>>(
                _ => new CapturingEventHandler<OrderPlaced>(receivedB)),
            serviceName: "service-b");

        await hostA.StartAsync(ct);
        await hostB.StartAsync(ct);
        await Task.Delay(500, ct);

        var transport = hostA.Services.GetRequiredService<ITransport>();
        await transport.SendAsync(BuildOutboxMessage(new OrderPlaced("order-fanout"), MessageKind.Event), ct);

        await WaitForConditionAsync(() => receivedA.Count == 1 && receivedB.Count == 1, ct);

        Assert.Single(receivedA);
        Assert.Single(receivedB);

        await hostA.StopAsync(ct);
        await hostB.StopAsync(ct);
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
        await transport.SendAsync(BuildOutboxMessage(new ProcessPayment("payment-1", 99m), MessageKind.Command), ct);

        await WaitForConditionAsync(() => received.Count == 1, ct);

        Assert.Single(received);
        Assert.Equal("payment-1", received[0].PaymentId);

        await host.StopAsync(ct);
    }

    // --- delivery hardening tests ---

    [Fact]
    public async Task Failed_handler_routes_message_to_dlq()
    {
        var ct = TestContext.Current.CancellationToken;

        using var host = BuildHost(services =>
        {
            services.AddScoped<IEventHandler<OrderPlaced>>(_ => new ThrowingEventHandler());
        });

        await host.StartAsync(ct);
        await Task.Delay(500, ct);

        var transport = host.Services.GetRequiredService<ITransport>();
        await transport.SendAsync(BuildOutboxMessage(new OrderPlaced("dlq-test"), MessageKind.Event), ct);

        // After nack the message should appear in the DLQ. Poll via direct BasicGet.
        string queueName = $"test-service-{_testRunId}.order-placed";
        string dlqName = $"{queueName}.dlq";
        await WaitForDlqMessageAsync(dlqName, ct);

        await host.StopAsync(ct);
    }

    // --- helpers ---

    private IHost BuildHost(
        Action<IServiceCollection>? configureExtra = null,
        string serviceName = "test-service")
        => Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddOpinionatedEventing();
                services.AddRabbitMQTransport(o =>
                {
                    o.ConnectionString = _fixture.ConnectionString;
                    o.ServiceName = $"{serviceName}-{_testRunId}";
                    o.AutoDeclareTopology = true;
                });
                configureExtra?.Invoke(services);
            })
            .Build();

    private static OutboxMessage BuildOutboxMessage<T>(T payload, MessageKind kind) where T : notnull
        => new()
        {
            Id = Guid.NewGuid(),
            MessageType = typeof(T).AssemblyQualifiedName!,
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

    private sealed class ThrowingEventHandler : IEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced @event, CancellationToken ct)
            => throw new InvalidOperationException("Deliberate handler failure for DLQ test.");
    }

    private async Task WaitForDlqMessageAsync(string dlqName, CancellationToken ct, int timeoutMs = 15_000)
    {
        var factory = new RabbitMqClient.ConnectionFactory { Uri = new Uri(_fixture.ConnectionString) };
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, ct);
            try
            {
                var result = await channel.BasicGetAsync(dlqName, autoAck: false, ct);
                if (result is not null)
                    return;
            }
            catch (RabbitMqClient.Exceptions.OperationInterruptedException) { }
        }

        Assert.Fail($"DLQ '{dlqName}' did not receive a message within the timeout.");
    }

}
