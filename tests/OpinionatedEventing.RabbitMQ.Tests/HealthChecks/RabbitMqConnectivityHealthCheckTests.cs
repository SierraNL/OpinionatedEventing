#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpinionatedEventing.RabbitMQ;
using OpinionatedEventing.RabbitMQ.HealthChecks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace OpinionatedEventing.RabbitMQ.Tests.HealthChecks;

public sealed class RabbitMqConnectivityHealthCheckTests
{
    [Fact]
    public void AddRabbitMqConnectivityHealthCheck_registers_check()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddRabbitMqConnectivityHealthCheck();

        var sp = services.BuildServiceProvider();
        var registrations = sp.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        Assert.Contains(registrations, r => r.Name == "opinionatedeventing-rabbitmq");
    }

    [Fact]
    public void AddRabbitMqConnectivityHealthCheck_check_has_live_and_broker_tags()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddRabbitMqConnectivityHealthCheck();

        var sp = services.BuildServiceProvider();
        var registration = sp.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(r => r.Name == "opinionatedeventing-rabbitmq");

        Assert.Contains("live", registration.Tags);
        Assert.Contains("broker", registration.Tags);
    }

    // ─── Behaviour tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckHealthAsync_returns_Unhealthy_when_connection_not_yet_established()
    {
        var ct = TestContext.Current.CancellationToken;
        var holder = new RabbitMqConnectionHolder(); // never set
        var check = new RabbitMqConnectivityHealthCheck(holder);

        var result = await check.CheckHealthAsync(MakeContext(), ct);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_returns_Healthy_when_connection_is_open()
    {
        var ct = TestContext.Current.CancellationToken;
        var holder = new RabbitMqConnectionHolder();
        holder.SetConnection(new FakeConnection(isOpen: true));
        var check = new RabbitMqConnectivityHealthCheck(holder);

        var result = await check.CheckHealthAsync(MakeContext(), ct);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_returns_Unhealthy_when_connection_is_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var holder = new RabbitMqConnectionHolder();
        holder.SetConnection(new FakeConnection(isOpen: false));
        var check = new RabbitMqConnectivityHealthCheck(holder);

        var result = await check.CheckHealthAsync(MakeContext(), ct);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    private static HealthCheckContext MakeContext() => new()
    {
        Registration = new HealthCheckRegistration("test", _ => null!, HealthStatus.Unhealthy, []),
    };

    private sealed class FakeConnection(bool isOpen) : IConnection
    {
        public bool IsOpen => isOpen;

        public ushort ChannelMax => 0;
        public IDictionary<string, object?> ClientProperties => new Dictionary<string, object?>();
        public string? ClientProvidedName => null;
        public ShutdownEventArgs? CloseReason => null;
        public AmqpTcpEndpoint Endpoint => new("localhost");
        public uint FrameMax => 0;
        public TimeSpan Heartbeat => TimeSpan.Zero;
        public IProtocol Protocol => throw new NotImplementedException();
        public IDictionary<string, object?> ServerProperties => new Dictionary<string, object?>();
        public IEnumerable<ShutdownReportEntry> ShutdownReport => [];
        public int LocalPort => 0;
        public int RemotePort => 0;

        public event AsyncEventHandler<CallbackExceptionEventArgs>? CallbackExceptionAsync { add { } remove { } }
        public event AsyncEventHandler<ShutdownEventArgs>? ConnectionShutdownAsync { add { } remove { } }
        public event AsyncEventHandler<AsyncEventArgs>? RecoverySucceededAsync { add { } remove { } }
        public event AsyncEventHandler<ConnectionRecoveryErrorEventArgs>? ConnectionRecoveryErrorAsync { add { } remove { } }
        public event AsyncEventHandler<ConsumerTagChangedAfterRecoveryEventArgs>? ConsumerTagChangeAfterRecoveryAsync { add { } remove { } }
        public event AsyncEventHandler<QueueNameChangedAfterRecoveryEventArgs>? QueueNameChangedAfterRecoveryAsync { add { } remove { } }
        public event AsyncEventHandler<RecoveringConsumerEventArgs>? RecoveringConsumerAsync { add { } remove { } }
        public event AsyncEventHandler<ConnectionBlockedEventArgs>? ConnectionBlockedAsync { add { } remove { } }
        public event AsyncEventHandler<AsyncEventArgs>? ConnectionUnblockedAsync { add { } remove { } }

        public Task CloseAsync(ushort reasonCode, string reasonText, TimeSpan timeout, bool abort,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IChannel> CreateChannelAsync(CreateChannelOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateSecretAsync(string newSecret, string reason,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }
}
