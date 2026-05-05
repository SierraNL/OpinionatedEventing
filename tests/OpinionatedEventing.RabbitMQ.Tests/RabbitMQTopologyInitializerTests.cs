#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MSOptions = Microsoft.Extensions.Options.Options;
using OpinionatedEventing.DependencyInjection;
using OpinionatedEventing.RabbitMQ;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace OpinionatedEventing.RabbitMQ.Tests;

public sealed class RabbitMQTopologyInitializerTests
{
    private static (RabbitMQTopologyInitializer Initializer, TopologyRecordingChannel Channel)
        CreateInitializer(
            Action<IServiceCollection>? configure = null,
            string serviceName = "test-svc",
            bool autoDeclare = true)
    {
        var channel = new TopologyRecordingChannel();
        var holder = new RabbitMqConnectionHolder();
        holder.SetConnection(new StubTopologyConnection(channel));

        var services = new ServiceCollection();
        configure?.Invoke(services);

        var registry = new MessageHandlerRegistry();

        var options = MSOptions.Create(new RabbitMQOptions
        {
            ConnectionString = "amqp://localhost",
            ServiceName = serviceName,
            AutoDeclareTopology = autoDeclare,
        });

        return (
            new RabbitMQTopologyInitializer(
                holder, registry, services, options,
                NullLogger<RabbitMQTopologyInitializer>.Instance),
            channel);
    }

    [Fact]
    public async Task StartAsync_skips_all_declarations_when_AutoDeclareTopology_is_false()
    {
        var ct = TestContext.Current.CancellationToken;
        var (initializer, channel) = CreateInitializer(
            configure: s => s.AddScoped<IEventHandler<TopologyTestEvent>>(_ => null!),
            autoDeclare: false);

        await initializer.StartAsync(ct);

        Assert.Empty(channel.DeclaredExchanges);
        Assert.Empty(channel.DeclaredQueues);
    }

    [Fact]
    public async Task StartAsync_declares_fanout_exchange_for_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var (initializer, channel) = CreateInitializer(
            s => s.AddScoped<IEventHandler<TopologyTestEvent>>(_ => null!));

        await initializer.StartAsync(ct);

        Assert.True(channel.DeclaredExchanges.ContainsKey("topology-test-event"));
        Assert.Equal(ExchangeType.Fanout, channel.DeclaredExchanges["topology-test-event"]);
    }

    [Fact]
    public async Task StartAsync_declares_event_consumer_queue_with_dlx_arguments()
    {
        var ct = TestContext.Current.CancellationToken;
        var (initializer, channel) = CreateInitializer(
            s => s.AddScoped<IEventHandler<TopologyTestEvent>>(_ => null!));

        await initializer.StartAsync(ct);

        const string queueName = "test-svc.topology-test-event";
        Assert.True(channel.DeclaredQueues.ContainsKey(queueName));
        var args = channel.DeclaredQueues[queueName];
        Assert.Equal($"{queueName}.dlx", args["x-dead-letter-exchange"]);
        Assert.Equal(queueName, args["x-dead-letter-routing-key"]);
    }

    [Fact]
    public async Task StartAsync_declares_dlx_exchange_and_dlq_for_event_queue()
    {
        var ct = TestContext.Current.CancellationToken;
        var (initializer, channel) = CreateInitializer(
            s => s.AddScoped<IEventHandler<TopologyTestEvent>>(_ => null!));

        await initializer.StartAsync(ct);

        const string queueName = "test-svc.topology-test-event";
        Assert.True(channel.DeclaredExchanges.ContainsKey($"{queueName}.dlx"));
        Assert.Equal(ExchangeType.Direct, channel.DeclaredExchanges[$"{queueName}.dlx"]);
        Assert.True(channel.DeclaredQueues.ContainsKey($"{queueName}.dlq"));
    }

    [Fact]
    public async Task StartAsync_binds_dlq_to_dlx_with_queue_name_as_routing_key_for_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var (initializer, channel) = CreateInitializer(
            s => s.AddScoped<IEventHandler<TopologyTestEvent>>(_ => null!));

        await initializer.StartAsync(ct);

        const string queueName = "test-svc.topology-test-event";
        Assert.Contains(channel.Bindings,
            b => b.Queue == $"{queueName}.dlq"
              && b.Exchange == $"{queueName}.dlx"
              && b.RoutingKey == queueName);
    }

    [Fact]
    public async Task StartAsync_declares_direct_exchange_for_command()
    {
        var ct = TestContext.Current.CancellationToken;
        var (initializer, channel) = CreateInitializer(
            s => s.AddScoped<ICommandHandler<TopologyTestCommand>>(_ => null!));

        await initializer.StartAsync(ct);

        Assert.True(channel.DeclaredExchanges.ContainsKey("topology-test-command"));
        Assert.Equal(ExchangeType.Direct, channel.DeclaredExchanges["topology-test-command"]);
    }

    [Fact]
    public async Task StartAsync_declares_command_queue_with_dlx_arguments()
    {
        var ct = TestContext.Current.CancellationToken;
        var (initializer, channel) = CreateInitializer(
            s => s.AddScoped<ICommandHandler<TopologyTestCommand>>(_ => null!));

        await initializer.StartAsync(ct);

        const string queueName = "topology-test-command";
        Assert.True(channel.DeclaredQueues.ContainsKey(queueName));
        var args = channel.DeclaredQueues[queueName];
        Assert.Equal($"{queueName}.dlx", args["x-dead-letter-exchange"]);
        Assert.Equal(queueName, args["x-dead-letter-routing-key"]);
    }

    [Fact]
    public async Task StartAsync_declares_dlx_exchange_and_dlq_for_command_queue()
    {
        var ct = TestContext.Current.CancellationToken;
        var (initializer, channel) = CreateInitializer(
            s => s.AddScoped<ICommandHandler<TopologyTestCommand>>(_ => null!));

        await initializer.StartAsync(ct);

        const string queueName = "topology-test-command";
        Assert.True(channel.DeclaredExchanges.ContainsKey($"{queueName}.dlx"));
        Assert.Equal(ExchangeType.Direct, channel.DeclaredExchanges[$"{queueName}.dlx"]);
        Assert.True(channel.DeclaredQueues.ContainsKey($"{queueName}.dlq"));
    }

    [Fact]
    public async Task StartAsync_binds_dlq_to_dlx_with_queue_name_as_routing_key_for_command()
    {
        var ct = TestContext.Current.CancellationToken;
        var (initializer, channel) = CreateInitializer(
            s => s.AddScoped<ICommandHandler<TopologyTestCommand>>(_ => null!));

        await initializer.StartAsync(ct);

        const string queueName = "topology-test-command";
        Assert.Contains(channel.Bindings,
            b => b.Queue == $"{queueName}.dlq"
              && b.Exchange == $"{queueName}.dlx"
              && b.RoutingKey == queueName);
    }

    [Fact]
    public async Task StartAsync_skips_queue_but_declares_exchange_when_ServiceName_is_empty()
    {
        var ct = TestContext.Current.CancellationToken;
        var (initializer, channel) = CreateInitializer(
            configure: s => s.AddScoped<IEventHandler<TopologyTestEvent>>(_ => null!),
            serviceName: "");

        await initializer.StartAsync(ct);

        Assert.True(channel.DeclaredExchanges.ContainsKey("topology-test-event"));
        Assert.DoesNotContain(channel.DeclaredQueues.Keys, k => k.Contains("topology-test-event"));
    }

    // --- test message types ---

    private sealed record TopologyTestEvent : IEvent;
    private sealed record TopologyTestCommand : ICommand;

    // --- test doubles ---

    private sealed class StubTopologyConnection : IConnection
    {
        private readonly IChannel _channel;
        public StubTopologyConnection(IChannel channel) => _channel = channel;
        public Task<IChannel> CreateChannelAsync(CreateChannelOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(_channel);

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
        public Task CloseAsync(ushort reasonCode, string reasonText, TimeSpan timeout, bool abort, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateSecretAsync(string newSecret, string reason, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class TopologyRecordingChannel : IChannel
    {
        public Dictionary<string, string> DeclaredExchanges { get; } = new();
        public Dictionary<string, IDictionary<string, object?>> DeclaredQueues { get; } = new();
        public List<(string Queue, string Exchange, string RoutingKey)> Bindings { get; } = new();

        public bool IsOpen => true;
        public bool IsClosed => false;
        public int ChannelNumber => 1;
        public ShutdownEventArgs? CloseReason => null;
        public ulong NextPublishSeqNo => 0;
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

        public Task ExchangeDeclareAsync(string exchange, string type, bool durable = false,
            bool autoDelete = false, IDictionary<string, object?>? arguments = null,
            bool passive = false, bool noWait = false, CancellationToken ct = default)
        {
            DeclaredExchanges[exchange] = type;
            return Task.CompletedTask;
        }

        public Task<QueueDeclareOk> QueueDeclareAsync(string queue = "", bool durable = false,
            bool exclusive = true, bool autoDelete = true, IDictionary<string, object?>? arguments = null,
            bool passive = false, bool noWait = false, CancellationToken ct = default)
        {
            DeclaredQueues[queue] = arguments ?? new Dictionary<string, object?>();
            return Task.FromResult(new QueueDeclareOk(queue, 0, 0));
        }

        public Task QueueBindAsync(string queue, string exchange, string routingKey,
            IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken ct = default)
        {
            Bindings.Add((queue, exchange, routingKey));
            return Task.CompletedTask;
        }

        public ValueTask BasicPublishAsync<TProperties>(string exchange, string routingKey, bool mandatory,
            TProperties basicProperties, ReadOnlyMemory<byte> body, CancellationToken ct = default)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader => throw new NotImplementedException();
        public ValueTask BasicPublishAsync<TProperties>(CachedString exchange, CachedString routingKey, bool mandatory,
            TProperties basicProperties, ReadOnlyMemory<byte> body, CancellationToken ct = default)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader => throw new NotImplementedException();
        public ValueTask<ulong> GetNextPublishSequenceNumberAsync(CancellationToken ct = default) => ValueTask.FromResult(0UL);
        public Task AbortAsync(ushort replyCode = 200, string replyText = "Goodbye", CancellationToken ct = default) => Task.CompletedTask;
        public Task CloseAsync(ushort replyCode = 200, string replyText = "Goodbye", bool abort = false, CancellationToken ct = default) => Task.CompletedTask;
        public Task CloseAsync(ShutdownEventArgs reason, bool abort, CancellationToken ct = default) => Task.CompletedTask;
        public Task CloseAsync(ShutdownEventArgs reason, bool abort) => Task.CompletedTask;
        public Task<string> BasicConsumeAsync(string queue, bool autoAck, string consumerTag,
            bool noLocal, bool exclusive, IDictionary<string, object?>? arguments,
            IAsyncBasicConsumer consumer, CancellationToken ct = default) => throw new NotImplementedException();
        public Task BasicCancelAsync(string tag, bool noWait = false, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask BasicAckAsync(ulong deliveryTag, bool multiple, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask BasicNackAsync(ulong deliveryTag, bool multiple, bool requeue, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask BasicRejectAsync(ulong deliveryTag, bool requeue, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<BasicGetResult?> BasicGetAsync(string queue, bool autoAck, CancellationToken ct = default) => throw new NotImplementedException();
        public Task BasicQosAsync(uint prefetchSize, ushort prefetchCount, bool global, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ExchangeDeclarePassiveAsync(string exchange, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ExchangeDeleteAsync(string exchange, bool ifUnused = false, bool noWait = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ExchangeBindAsync(string destination, string source, string routingKey,
            IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ExchangeUnbindAsync(string destination, string source, string routingKey,
            IDictionary<string, object?>? arguments = null, bool noWait = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<QueueDeclareOk> QueueDeclarePassiveAsync(string queue, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<uint> QueueDeleteAsync(string queue, bool ifUnused = false, bool ifEmpty = false,
            bool noWait = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<uint> MessageCountAsync(string queue, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<uint> ConsumerCountAsync(string queue, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<uint> QueuePurgeAsync(string queue, CancellationToken ct = default) => throw new NotImplementedException();
        public Task QueueUnbindAsync(string queue, string exchange, string routingKey,
            IDictionary<string, object?>? arguments = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task TxSelectAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task TxCommitAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task TxRollbackAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> WaitForConfirmsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task WaitForConfirmsOrDieAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task ConfirmSelectAsync(bool trackConfirmations = true, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }
}
