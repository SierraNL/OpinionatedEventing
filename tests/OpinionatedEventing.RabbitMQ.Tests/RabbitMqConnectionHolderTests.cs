#nullable enable

using OpinionatedEventing.RabbitMQ;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace OpinionatedEventing.RabbitMQ.Tests;

public sealed class RabbitMqConnectionHolderTests
{
    [Fact]
    public async Task GetConnectionAsync_returns_connection_after_SetConnection()
    {
        var holder = new RabbitMqConnectionHolder();
        var fakeConnection = new FakeConnection();

        holder.SetConnection(fakeConnection);

        var result = await holder.GetConnectionAsync(CancellationToken.None);

        Assert.Same(fakeConnection, result);
    }

    [Fact]
    public async Task GetConnectionAsync_awaits_until_connection_is_set()
    {
        var holder = new RabbitMqConnectionHolder();
        var fakeConnection = new FakeConnection();

        var task = holder.GetConnectionAsync(CancellationToken.None);
        Assert.False(task.IsCompleted);

        holder.SetConnection(fakeConnection);
        var result = await task;

        Assert.Same(fakeConnection, result);
    }

    [Fact]
    public async Task GetConnectionAsync_propagates_exception_from_SetException()
    {
        var holder = new RabbitMqConnectionHolder();
        var expected = new InvalidOperationException("broker down");

        holder.SetException(expected);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => holder.GetConnectionAsync(CancellationToken.None));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task GetConnectionAsync_throws_OperationCanceledException_when_cancelled()
    {
        var holder = new RabbitMqConnectionHolder();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => holder.GetConnectionAsync(cts.Token));
    }

    [Fact]
    public void TryGetConnection_returns_null_before_connection_is_set()
    {
        var holder = new RabbitMqConnectionHolder();

        Assert.Null(holder.TryGetConnection());
    }

    [Fact]
    public void TryGetConnection_returns_connection_after_SetConnection()
    {
        var holder = new RabbitMqConnectionHolder();
        var fakeConnection = new FakeConnection();

        holder.SetConnection(fakeConnection);

        Assert.Same(fakeConnection, holder.TryGetConnection());
    }

    [Fact]
    public void TryGetConnection_returns_null_when_holder_faulted()
    {
        var holder = new RabbitMqConnectionHolder();
        holder.SetException(new Exception("boom"));

        Assert.Null(holder.TryGetConnection());
    }

    // ─── Minimal fake ─────────────────────────────────────────────────────────────

    private sealed class FakeConnection : IConnection
    {
        public ushort ChannelMax => 0;
        public IDictionary<string, object?> ClientProperties => new Dictionary<string, object?>();
        public string? ClientProvidedName => null;
        public ShutdownEventArgs? CloseReason => null;
        public AmqpTcpEndpoint Endpoint => new("localhost");
        public uint FrameMax => 0;
        public TimeSpan Heartbeat => TimeSpan.Zero;
        public bool IsOpen => true;
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
