#nullable enable

using Microsoft.Extensions.Logging.Abstractions;
using MSOptions = Microsoft.Extensions.Options.Options;
using OpinionatedEventing.Options;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.RabbitMQ;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace OpinionatedEventing.RabbitMQ.Tests;

public sealed class RabbitMQTransportTests
{
    private static RabbitMQTransport CreateTransport(
        PublishRecordingChannel channel,
        int concurrentWorkers = 1)
    {
        MessageTypeRegistry registry = new();
        registry.Register(typeof(TestEvent));
        registry.Register(typeof(TestCommand));

        RabbitMqConnectionHolder holder = new();
        holder.SetConnection(new StubConnection(channel));

        return new RabbitMQTransport(
            connectionHolder: holder,
            registry: registry,
            outboxOptions: MSOptions.Create(new OutboxOptions { ConcurrentWorkers = concurrentWorkers }),
            logger: NullLogger<RabbitMQTransport>.Instance);
    }

    private static OutboxMessage BuildMessage(string kind, Type type) => new()
    {
        Id = Guid.NewGuid(),
        MessageType = type.FullName!,
        MessageKind = kind,
        Payload = "{}",
        CorrelationId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // ── happy-path routing ─────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_publishes_event_to_exchange_with_empty_routing_key()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        PublishRecordingChannel channel = new();
        await using RabbitMQTransport transport = CreateTransport(channel);

        await transport.SendAsync(BuildMessage("Event", typeof(TestEvent)), ct);

        Assert.Equal("test-event", channel.LastExchange);
        Assert.Equal(string.Empty, channel.LastRoutingKey);
        Assert.True(channel.LastMandatory);
    }

    [Fact]
    public async Task SendAsync_publishes_command_to_queue_exchange_with_routing_key()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        PublishRecordingChannel channel = new();
        await using RabbitMQTransport transport = CreateTransport(channel);

        await transport.SendAsync(BuildMessage("Command", typeof(TestCommand)), ct);

        Assert.Equal("test-command", channel.LastExchange);
        Assert.Equal("test-command", channel.LastRoutingKey);
        Assert.True(channel.LastMandatory);
    }

    // ── confirm gate ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_throws_when_BasicPublishAsync_throws()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        PublishRecordingChannel channel = new() { PublishException = new InvalidOperationException("broker nack") };
        await using RabbitMQTransport transport = CreateTransport(channel);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.SendAsync(BuildMessage("Event", typeof(TestEvent)), ct));
    }

    [Fact]
    public async Task SendAsync_throws_PublishReturnException_when_message_unroutable()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        // Simulate what the library does: throw PublishReturnException from BasicPublishAsync
        // when mandatory=true and the broker returns the message.
        PublishReturnException returnEx = new(1UL, "Returned", "test-event", "", 312, "NO_ROUTE");
        PublishRecordingChannel channel = new() { PublishException = returnEx };
        await using RabbitMQTransport transport = CreateTransport(channel);

        await Assert.ThrowsAsync<PublishReturnException>(
            () => transport.SendAsync(BuildMessage("Event", typeof(TestEvent)), ct));
    }

    // ── channel pool ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_reuses_channel_across_sequential_calls()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        PublishRecordingChannel channel = new();
        StubConnection connection = new(channel);
        MessageTypeRegistry registry = new();
        registry.Register(typeof(TestEvent));
        RabbitMqConnectionHolder holder = new();
        holder.SetConnection(connection);

        await using RabbitMQTransport transport = new(
            holder, registry,
            MSOptions.Create(new OutboxOptions { ConcurrentWorkers = 1 }),
            NullLogger<RabbitMQTransport>.Instance);

        await transport.SendAsync(BuildMessage("Event", typeof(TestEvent)), ct);
        await transport.SendAsync(BuildMessage("Event", typeof(TestEvent)), ct);

        Assert.Equal(1, connection.ChannelsCreated);
    }

    [Fact]
    public async Task SendAsync_discards_faulted_channel_and_creates_new_one_next_call()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        // First call: channel throws. Second call: new channel succeeds.
        PublishRecordingChannel firstChannel = new() { PublishException = new Exception("simulated nack") };
        PublishRecordingChannel secondChannel = new();
        CountingConnection connection = new(firstChannel, secondChannel);
        MessageTypeRegistry registry = new();
        registry.Register(typeof(TestEvent));
        RabbitMqConnectionHolder holder = new();
        holder.SetConnection(connection);

        await using RabbitMQTransport transport = new(
            holder, registry,
            MSOptions.Create(new OutboxOptions { ConcurrentWorkers = 1 }),
            NullLogger<RabbitMQTransport>.Instance);

        await Assert.ThrowsAsync<Exception>(
            () => transport.SendAsync(BuildMessage("Event", typeof(TestEvent)), ct));

        // Second publish succeeds with a freshly created channel
        await transport.SendAsync(BuildMessage("Event", typeof(TestEvent)), ct);

        Assert.Equal(2, connection.ChannelsCreated);
        Assert.Equal(1, secondChannel.PublishCount);
    }

    // ── test doubles ──────────────────────────────────────────────────────────

    private sealed record TestEvent : IEvent;
    private sealed record TestCommand : ICommand;

    private sealed class PublishRecordingChannel : IChannel
    {
        public string? LastExchange { get; private set; }
        public string? LastRoutingKey { get; private set; }
        public bool LastMandatory { get; private set; }
        public int PublishCount { get; private set; }
        public Exception? PublishException { get; init; }

        public bool IsOpen => true;
        public bool IsClosed => false;
        public int ChannelNumber => 1;
        public ShutdownEventArgs? CloseReason => null;
        public ulong NextPublishSeqNo => 1;
        public string? CurrentQueue => null;
        public IAsyncBasicConsumer? DefaultConsumer { get => null; set { } }
        public TimeSpan ContinuationTimeout { get => TimeSpan.Zero; set { } }

        public event AsyncEventHandler<BasicAckEventArgs>? BasicAcksAsync { add { } remove { } }
        public event AsyncEventHandler<BasicNackEventArgs>? BasicNacksAsync { add { } remove { } }
#pragma warning disable CS0067
        public event AsyncEventHandler<BasicReturnEventArgs>? BasicReturnAsync;
#pragma warning restore CS0067
        public event AsyncEventHandler<CallbackExceptionEventArgs>? CallbackExceptionAsync { add { } remove { } }
        public event AsyncEventHandler<FlowControlEventArgs>? FlowControlAsync { add { } remove { } }
        public event AsyncEventHandler<ShutdownEventArgs>? ChannelShutdownAsync { add { } remove { } }

        public async ValueTask BasicPublishAsync<TProperties>(
            string exchange, string routingKey, bool mandatory,
            TProperties basicProperties, ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader
        {
            LastExchange = exchange;
            LastRoutingKey = routingKey;
            LastMandatory = mandatory;
            PublishCount++;

            if (PublishException is not null)
                throw PublishException;
        }

        public ValueTask BasicPublishAsync<TProperties>(
            CachedString exchange, CachedString routingKey, bool mandatory,
            TProperties basicProperties, ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader
            => throw new NotImplementedException();

        public ValueTask<ulong> GetNextPublishSequenceNumberAsync(CancellationToken ct = default)
            => ValueTask.FromResult(NextPublishSeqNo);

        public Task AbortAsync(ushort replyCode = 200, string replyText = "Goodbye",
            CancellationToken ct = default) => Task.CompletedTask;
        public Task CloseAsync(ushort replyCode = 200, string replyText = "Goodbye", bool abort = false,
            CancellationToken ct = default) => Task.CompletedTask;
        public Task CloseAsync(ShutdownEventArgs reason, bool abort, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task CloseAsync(ShutdownEventArgs reason, bool abort) => Task.CompletedTask;

        public Task<string> BasicConsumeAsync(string queue, bool autoAck, string consumerTag,
            bool noLocal, bool exclusive, IDictionary<string, object?>? arguments,
            IAsyncBasicConsumer consumer, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task BasicCancelAsync(string tag, bool noWait = false, CancellationToken ct = default)
            => throw new NotImplementedException();
        public ValueTask BasicAckAsync(ulong deliveryTag, bool multiple, CancellationToken ct = default)
            => throw new NotImplementedException();
        public ValueTask BasicNackAsync(ulong deliveryTag, bool multiple, bool requeue, CancellationToken ct = default)
            => throw new NotImplementedException();
        public ValueTask BasicRejectAsync(ulong deliveryTag, bool requeue, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<BasicGetResult?> BasicGetAsync(string queue, bool autoAck, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task BasicQosAsync(uint prefetchSize, ushort prefetchCount, bool global, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task ExchangeDeclareAsync(string exchange, string type, bool durable = false,
            bool autoDelete = false, IDictionary<string, object?>? arguments = null,
            bool passive = false, bool noWait = false, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task ExchangeDeclarePassiveAsync(string exchange, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task ExchangeDeleteAsync(string exchange, bool ifUnused = false, bool noWait = false,
            CancellationToken ct = default) => throw new NotImplementedException();
        public Task ExchangeBindAsync(string destination, string source, string routingKey,
            IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task ExchangeUnbindAsync(string destination, string source, string routingKey,
            IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<QueueDeclareOk> QueueDeclareAsync(string queue = "", bool durable = false,
            bool exclusive = true, bool autoDelete = true, IDictionary<string, object?>? arguments = null,
            bool passive = false, bool noWait = false, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<QueueDeclareOk> QueueDeclarePassiveAsync(string queue, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task QueueBindAsync(string queue, string exchange, string routingKey,
            IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<uint> QueueDeleteAsync(string queue, bool ifUnused = false, bool ifEmpty = false,
            bool noWait = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<uint> MessageCountAsync(string queue, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<uint> ConsumerCountAsync(string queue, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<uint> QueuePurgeAsync(string queue, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task QueueUnbindAsync(string queue, string exchange, string routingKey,
            IDictionary<string, object?>? arguments = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task TxSelectAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task TxCommitAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task TxRollbackAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> WaitForConfirmsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task WaitForConfirmsOrDieAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task ConfirmSelectAsync(bool trackConfirmations = true, CancellationToken ct = default) => throw new NotImplementedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class StubConnection : IConnection
    {
        private readonly IChannel _channel;
        public int ChannelsCreated { get; private set; }

        public StubConnection(IChannel channel) => _channel = channel;

        public Task<IChannel> CreateChannelAsync(CreateChannelOptions? options = null, CancellationToken ct = default)
        {
            ChannelsCreated++;
            return Task.FromResult(_channel);
        }

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
            CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateSecretAsync(string newSecret, string reason, CancellationToken ct = default)
            => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class CountingConnection : IConnection
    {
        private readonly IChannel[] _channels;
        private int _index;
        public int ChannelsCreated { get; private set; }

        public CountingConnection(params IChannel[] channels) => _channels = channels;

        public Task<IChannel> CreateChannelAsync(CreateChannelOptions? options = null, CancellationToken ct = default)
        {
            ChannelsCreated++;
            return Task.FromResult(_channels[_index++]);
        }

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

        public Task CloseAsync(ushort replyCode, string replyText, TimeSpan timeout, bool abort,
            CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateSecretAsync(string newSecret, string reason, CancellationToken ct = default)
            => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }
}
