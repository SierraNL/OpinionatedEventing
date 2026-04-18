#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    public async Task Dead_lettered_message_is_recorded_in_outbox()
    {
        var ct = TestContext.Current.CancellationToken;
        var outboxStore = new InMemoryOutboxStore();

        using var host = BuildHost(services =>
        {
            services.AddScoped<IEventHandler<OrderPlaced>>(_ => new ThrowingHandler<OrderPlaced>());
            services.AddSingleton<IOutboxStore>(outboxStore);
        }, maxDeliveryCount: 1);

        await host.StartAsync(ct);
        await Task.Delay(500, ct);

        var transport = host.Services.GetRequiredService<ITransport>();
        await transport.SendAsync(BuildOutboxMessage(new OrderPlaced("dead-order"), "Event"), ct);

        await WaitForConditionAsync(() => outboxStore.Messages.Any(m => m.FailedAt.HasValue), ct);

        var deadLetter = outboxStore.Messages.Single(m => m.FailedAt.HasValue);
        Assert.NotNull(deadLetter.Error);

        await host.StopAsync(ct);
    }

    // --- helpers ---

    private IHost BuildHost(Action<IServiceCollection>? configureExtra = null, int maxDeliveryCount = 5)
        => Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddOpinionatedEventing();
                services.AddAzureServiceBusTransport(o =>
                {
                    o.ConnectionString = _fixture.ConnectionString;
                    o.ServiceName = "test-service";
                    o.AutoCreateResources = true;
                    o.MaxDeliveryCount = maxDeliveryCount;
                });
                configureExtra?.Invoke(services);
            })
            .Build();

    private static OutboxMessage BuildOutboxMessage<T>(T payload, string kind) where T : notnull
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

    private sealed class ThrowingHandler<T> : IEventHandler<T> where T : class, IEvent
    {
        public Task HandleAsync(T @event, CancellationToken ct)
            => throw new InvalidOperationException("Simulated handler failure.");
    }
}
