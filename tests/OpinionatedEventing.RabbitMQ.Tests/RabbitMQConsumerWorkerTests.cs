#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MSOptions = Microsoft.Extensions.Options.Options;
using OpinionatedEventing;
using OpinionatedEventing.DependencyInjection;
using OpinionatedEventing.RabbitMQ;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using Xunit;

namespace OpinionatedEventing.RabbitMQ.Tests;

public sealed class RabbitMQConsumerWorkerTests
{
    private static RabbitMQConsumerWorker CreateWorker(IMessageHandlerRunner runner, int maxDeliveryCount = 5)
    {
        var holder = new RabbitMqConnectionHolder();
        holder.SetConnection(new NeverCalledConnection());

        var options = MSOptions.Create(new RabbitMQOptions
        {
            ConnectionString = "amqp://localhost",
            MaxDeliveryCount = maxDeliveryCount,
        });

        return new RabbitMQConsumerWorker(
            connectionHolder: holder,
            handlerRunner: runner,
            scopeFactory: new NeverCalledScopeFactory(),
            registry: new MessageHandlerRegistry(),
            envelope: new DefaultRabbitMQMessageEnvelope(),
            options: options,
            pauseController: new FakeConsumerPauseController(startPaused: false),
            timeProvider: TimeProvider.System,
            logger: NullLogger<RabbitMQConsumerWorker>.Instance);
    }

    [Fact]
    public async Task ProcessDeliveryAsync_passes_inbound_MessageId_as_causationId()
    {
        var ct = TestContext.Current.CancellationToken;
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var runner = new RecordingHandlerRunner();
        var worker = CreateWorker(runner);
        var channel = new AckRecordingChannel();

        var props = new BasicProperties
        {
            MessageId = messageId.ToString(),
            Headers = new Dictionary<string, object?>
            {
                ["MessageType"] = typeof(object).AssemblyQualifiedName,
                ["MessageKind"] = "Event",
                ["CorrelationId"] = correlationId.ToString(),
            }
        };
        var ea = new BasicDeliverEventArgs(
            consumerTag: "",
            deliveryTag: 1,
            redelivered: false,
            exchange: "",
            routingKey: "",
            properties: props,
            body: Encoding.UTF8.GetBytes("{}"));

        await worker.ProcessDeliveryAsync(channel, "queue", ea, ct);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(messageId, call.CausationId);
        Assert.Equal(correlationId, call.CorrelationId);
        Assert.True(channel.Acked);
    }

    [Fact]
    public async Task ProcessDeliveryAsync_causationId_is_null_when_MessageId_is_not_a_guid()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.NewGuid();
        var runner = new RecordingHandlerRunner();
        var worker = CreateWorker(runner);
        var channel = new AckRecordingChannel();

        var props = new BasicProperties
        {
            MessageId = "not-a-guid",
            Headers = new Dictionary<string, object?>
            {
                ["MessageType"] = typeof(object).AssemblyQualifiedName,
                ["MessageKind"] = "Event",
                ["CorrelationId"] = correlationId.ToString(),
            }
        };
        var ea = new BasicDeliverEventArgs(
            consumerTag: "",
            deliveryTag: 1,
            redelivered: false,
            exchange: "",
            routingKey: "",
            properties: props,
            body: Encoding.UTF8.GetBytes("{}"));

        await worker.ProcessDeliveryAsync(channel, "queue", ea, ct);

        var call = Assert.Single(runner.Calls);
        Assert.Null(call.CausationId);
    }

    [Fact]
    public async Task ProcessDeliveryAsync_nacks_message_missing_required_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new RecordingHandlerRunner();
        var worker = CreateWorker(runner);
        var channel = new AckRecordingChannel();

        var ea = new BasicDeliverEventArgs(
            consumerTag: "",
            deliveryTag: 1,
            redelivered: false,
            exchange: "",
            routingKey: "",
            properties: new BasicProperties(),
            body: Encoding.UTF8.GetBytes("{}"));

        await worker.ProcessDeliveryAsync(channel, "queue", ea, ct);

        Assert.True(channel.Nacked);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task ProcessDeliveryAsync_republishes_to_same_queue_with_incremented_retry_count_on_handler_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.NewGuid();
        var runner = new ThrowingHandlerRunner();
        var worker = CreateWorker(runner, maxDeliveryCount: 5);
        var channel = new AckRecordingChannel();

        var props = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Headers = new Dictionary<string, object?>
            {
                ["MessageType"] = typeof(object).AssemblyQualifiedName,
                ["MessageKind"] = "Event",
                ["CorrelationId"] = correlationId.ToString(),
            }
        };
        var ea = new BasicDeliverEventArgs(
            consumerTag: "",
            deliveryTag: 1,
            redelivered: false,
            exchange: "",
            routingKey: "",
            properties: props,
            body: Encoding.UTF8.GetBytes("{}"));

        await worker.ProcessDeliveryAsync(channel, "orders.queue", ea, ct);

        Assert.True(channel.Acked);
        Assert.False(channel.Nacked);
        var publish = Assert.Single(channel.Publishes);
        Assert.Equal(string.Empty, publish.Exchange);
        Assert.Equal("orders.queue", publish.RoutingKey);
        Assert.Equal(1, publish.Properties.Headers?["x-retry-count"]);
    }

    [Fact]
    public async Task ProcessDeliveryAsync_dead_letters_once_max_delivery_count_is_reached()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.NewGuid();
        var runner = new ThrowingHandlerRunner();
        var worker = CreateWorker(runner, maxDeliveryCount: 3);
        var channel = new AckRecordingChannel();

        var props = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Headers = new Dictionary<string, object?>
            {
                ["MessageType"] = typeof(object).AssemblyQualifiedName,
                ["MessageKind"] = "Event",
                ["CorrelationId"] = correlationId.ToString(),
                ["x-retry-count"] = 2,
            }
        };
        var ea = new BasicDeliverEventArgs(
            consumerTag: "",
            deliveryTag: 1,
            redelivered: true,
            exchange: "",
            routingKey: "",
            properties: props,
            body: Encoding.UTF8.GetBytes("{}"));

        await worker.ProcessDeliveryAsync(channel, "orders.queue", ea, ct);

        Assert.True(channel.Acked);
        Assert.False(channel.Nacked);
        var publish = Assert.Single(channel.Publishes);
        Assert.Equal("orders.queue.dlx", publish.Exchange);
        Assert.Equal("orders.queue", publish.RoutingKey);
        Assert.Equal(3, publish.Properties.Headers?["x-retry-count"]);
        Assert.Equal("HandlerException", publish.Properties.Headers?["x-dead-letter-reason"]);
    }

    [Fact]
    public async Task ProcessDeliveryAsync_nacks_without_requeue_when_republish_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.NewGuid();
        var runner = new ThrowingHandlerRunner();
        var worker = CreateWorker(runner, maxDeliveryCount: 5);
        var channel = new AckRecordingChannel { PublishException = new InvalidOperationException("channel closed") };

        var props = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Headers = new Dictionary<string, object?>
            {
                ["MessageType"] = typeof(object).AssemblyQualifiedName,
                ["MessageKind"] = "Event",
                ["CorrelationId"] = correlationId.ToString(),
            }
        };
        var ea = new BasicDeliverEventArgs(
            consumerTag: "",
            deliveryTag: 1,
            redelivered: false,
            exchange: "",
            routingKey: "",
            properties: props,
            body: Encoding.UTF8.GetBytes("{}"));

        await worker.ProcessDeliveryAsync(channel, "orders.queue", ea, ct);

        Assert.True(channel.Nacked);
        Assert.False(channel.Acked);
        Assert.Empty(channel.Publishes);
    }

    [Fact]
    public async Task ProcessDeliveryAsync_swallows_exception_when_both_republish_and_nack_fail()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.NewGuid();
        var runner = new ThrowingHandlerRunner();
        var worker = CreateWorker(runner, maxDeliveryCount: 5);
        var channel = new AckRecordingChannel
        {
            PublishException = new InvalidOperationException("channel closed"),
            NackException = new InvalidOperationException("channel also closed"),
        };

        var props = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Headers = new Dictionary<string, object?>
            {
                ["MessageType"] = typeof(object).AssemblyQualifiedName,
                ["MessageKind"] = "Event",
                ["CorrelationId"] = correlationId.ToString(),
            }
        };
        var ea = new BasicDeliverEventArgs(
            consumerTag: "",
            deliveryTag: 1,
            redelivered: false,
            exchange: "",
            routingKey: "",
            properties: props,
            body: Encoding.UTF8.GetBytes("{}"));

        await worker.ProcessDeliveryAsync(channel, "orders.queue", ea, ct);

        Assert.False(channel.Acked);
        Assert.Empty(channel.Publishes);
    }

    // ─── Fakes ────────────────────────────────────────────────────────────────────

    private sealed class NeverCalledScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
            => throw new InvalidOperationException("Should not be called.");
    }

    private sealed record RunnerCall(
        string MessageType, string MessageKind, string Payload,
        Guid CorrelationId, Guid? CausationId);

    private sealed class RecordingHandlerRunner : IMessageHandlerRunner
    {
        public List<RunnerCall> Calls { get; } = [];

        public Task RunAsync(string messageType, string messageKind, string payload,
            Guid? messageId, Guid correlationId, Guid? causationId, CancellationToken ct)
        {
            Calls.Add(new RunnerCall(messageType, messageKind, payload, correlationId, causationId));
            return Task.CompletedTask;
        }
    }

    private sealed record RecordedPublish(string Exchange, string RoutingKey, IReadOnlyBasicProperties Properties);

    private sealed class ThrowingHandlerRunner : IMessageHandlerRunner
    {
        public Task RunAsync(string messageType, string messageKind, string payload,
            Guid? messageId, Guid correlationId, Guid? causationId, CancellationToken ct)
            => throw new InvalidOperationException("Simulated handler failure.");
    }

    private sealed class AckRecordingChannel : IChannel
    {
        public bool Acked { get; private set; }
        public bool Nacked { get; private set; }
        public List<RecordedPublish> Publishes { get; } = [];
        public Exception? PublishException { get; set; }
        public Exception? NackException { get; set; }

        public ValueTask BasicAckAsync(ulong deliveryTag, bool multiple,
            CancellationToken cancellationToken = default)
        {
            Acked = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask BasicNackAsync(ulong deliveryTag, bool multiple, bool requeue,
            CancellationToken cancellationToken = default)
        {
            Nacked = true;
            if (NackException is not null)
                throw NackException;
            return ValueTask.CompletedTask;
        }

        public ValueTask BasicPublishAsync<TProperties>(string exchange, string routingKey,
            bool mandatory, TProperties basicProperties, ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader
        {
            if (PublishException is not null)
                throw PublishException;
            Publishes.Add(new RecordedPublish(exchange, routingKey, basicProperties));
            return ValueTask.CompletedTask;
        }

        // ── remaining interface members (not called in ProcessDeliveryAsync) ──────

        public int ChannelNumber => 0;
        public ShutdownEventArgs? CloseReason => null;
        public bool IsOpen => true;
        public bool IsClosed => false;
        public ulong NextPublishSeqNo => 0;
        public string? CurrentQueue => null;
        public IAsyncBasicConsumer? DefaultConsumer { get => null; set { } }
        public TimeSpan ContinuationTimeout { get => TimeSpan.Zero; set { } }

        public event AsyncEventHandler<BasicAckEventArgs>? BasicAcksAsync { add { } remove { } }
        public event AsyncEventHandler<BasicNackEventArgs>? BasicNacksAsync { add { } remove { } }
        public event AsyncEventHandler<BasicReturnEventArgs>? BasicReturnAsync { add { } remove { } }
        public event AsyncEventHandler<CallbackExceptionEventArgs>? CallbackExceptionAsync { add { } remove { } }
        public event AsyncEventHandler<FlowControlEventArgs>? FlowControlAsync { add { } remove { } }
        public event AsyncEventHandler<ShutdownEventArgs>? ChannelShutdownAsync { add { } remove { } }

        public Task AbortAsync(ushort replyCode = 200, string replyText = "Goodbye",
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(ushort replyCode = 200, string replyText = "Goodbye", bool abort = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(ShutdownEventArgs reason, bool abort,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(ShutdownEventArgs reason, bool abort) => Task.CompletedTask;

        public ValueTask<ulong> GetNextPublishSequenceNumberAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(0UL);

        public Task<string> BasicConsumeAsync(string queue, bool autoAck, string consumerTag,
            bool noLocal, bool exclusive, IDictionary<string, object?>? arguments,
            IAsyncBasicConsumer consumer, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task BasicCancelAsync(string consumerTag, bool noWait = false,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BasicGetResult?> BasicGetAsync(string queue, bool autoAck,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask BasicPublishAsync<TProperties>(CachedString exchange, CachedString routingKey,
            bool mandatory, TProperties basicProperties, ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default)
            where TProperties : IReadOnlyBasicProperties, IAmqpHeader
            => throw new NotImplementedException();
        public Task BasicQosAsync(uint prefetchSize, ushort prefetchCount, bool global,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask BasicRejectAsync(ulong deliveryTag, bool requeue,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ConfirmSelectAsync(bool trackConfirmations = true,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExchangeBindAsync(string destination, string source, string routingKey,
            IDictionary<string, object?>? arguments = null, bool noWait = false,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExchangeDeclareAsync(string exchange, string type, bool durable = false,
            bool autoDelete = false, IDictionary<string, object?>? arguments = null,
            bool passive = false, bool noWait = false,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExchangeDeclarePassiveAsync(string exchange,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExchangeDeleteAsync(string exchange, bool ifUnused = false, bool noWait = false,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ExchangeUnbindAsync(string destination, string source, string routingKey,
            IDictionary<string, object?>? arguments = null, bool noWait = false,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<QueueDeclareOk> QueueDeclareAsync(string queue = "", bool durable = false,
            bool exclusive = true, bool autoDelete = true,
            IDictionary<string, object?>? arguments = null, bool passive = false,
            bool noWait = false, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<QueueDeclareOk> QueueDeclarePassiveAsync(string queue,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task QueueBindAsync(string queue, string exchange, string routingKey,
            IDictionary<string, object?>? arguments = null, bool noWait = false,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<uint> QueueDeleteAsync(string queue, bool ifUnused = false, bool ifEmpty = false,
            bool noWait = false, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<uint> MessageCountAsync(string queue,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<uint> ConsumerCountAsync(string queue,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<uint> QueuePurgeAsync(string queue,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task QueueUnbindAsync(string queue, string exchange, string routingKey,
            IDictionary<string, object?>? arguments = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task TxCommitAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task TxRollbackAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task TxSelectAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> WaitForConfirmsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task WaitForConfirmsOrDieAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class NeverCalledConnection : IConnection
    {
        public ushort ChannelMax => 0;
        public IDictionary<string, object?> ClientProperties => new Dictionary<string, object?>();
        public string? ClientProvidedName => null;
        public ShutdownEventArgs? CloseReason => null;
        public AmqpTcpEndpoint Endpoint => new("localhost");
        public uint FrameMax => 0;
        public TimeSpan Heartbeat => TimeSpan.Zero;
        public bool IsOpen => true;
        public IProtocol Protocol => throw new InvalidOperationException("Should not be called.");
        public IDictionary<string, object?> ServerProperties => new Dictionary<string, object?>();
        public IEnumerable<ShutdownReportEntry> ShutdownReport => Array.Empty<ShutdownReportEntry>();
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
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Should not be called in no-handler tests.");
        public Task UpdateSecretAsync(string newSecret, string reason,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }
}
