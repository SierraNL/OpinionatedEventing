#nullable enable

using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Options;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.RabbitMQ.Routing;
using RabbitMQ.Client;

namespace OpinionatedEventing.RabbitMQ;

/// <summary>
/// RabbitMQ implementation of <see cref="ITransport"/>.
/// Forwards outbox messages to the correct RabbitMQ exchange (events) or queue (commands).
/// </summary>
/// <remarks>
/// Each channel in the pool has publisher confirmations enabled via
/// <see cref="CreateChannelOptions"/>. <c>BasicPublishAsync</c>
/// awaits the broker ack before returning, so a failed publish throws and the outbox dispatcher
/// increments the attempt count rather than marking the message processed.
/// <para>
/// <c>mandatory: true</c> is set on every publish. When the broker cannot route a message it
/// returns it and the client library throws <c>PublishReturnException</c> from
/// <c>BasicPublishAsync</c>, propagating the failure to the outbox dispatcher.
/// </para>
/// The pool is sized by <see cref="OutboxOptions.ConcurrentWorkers"/> so each concurrent dispatch
/// worker gets its own channel.
/// </remarks>
internal sealed class RabbitMQTransport : ITransport, IAsyncDisposable
{
    private readonly RabbitMqConnectionHolder _connectionHolder;
    private readonly IMessageTypeRegistry _registry;
    private readonly ILogger<RabbitMQTransport> _logger;

    private IConnection? _connection;
    private readonly ConcurrentQueue<IChannel> _channelPool = new();
    private readonly SemaphoreSlim _poolSemaphore;

    private static readonly CreateChannelOptions ConfirmChannelOptions =
        new(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);

    /// <summary>Initialises a new <see cref="RabbitMQTransport"/>.</summary>
    public RabbitMQTransport(
        RabbitMqConnectionHolder connectionHolder,
        IMessageTypeRegistry registry,
        IOptions<OutboxOptions> outboxOptions,
        ILogger<RabbitMQTransport> logger)
    {
        _connectionHolder = connectionHolder;
        _registry = registry;
        _logger = logger;
        int poolSize = Math.Max(1, outboxOptions.Value.ConcurrentWorkers);
        _poolSemaphore = new SemaphoreSlim(poolSize, poolSize);
    }

    /// <inheritdoc/>
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        string exchange;
        string routingKey;

        if (message.MessageKind == MessageKind.Event)
        {
            Type type = _registry.Resolve(message.MessageType);
            exchange = MessageNamingConvention.GetExchangeName(type);
            routingKey = string.Empty;
        }
        else
        {
            Type type = _registry.Resolve(message.MessageType);
            exchange = MessageNamingConvention.GetQueueName(type);
            routingKey = MessageNamingConvention.GetQueueName(type);
        }

        ReadOnlyMemory<byte> body = Encoding.UTF8.GetBytes(message.Payload);
        BasicProperties properties = new()
        {
            MessageId = message.Id.ToString(),
            ContentType = "application/json",
            CorrelationId = message.CorrelationId.ToString(),
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?>
            {
                ["MessageType"] = message.MessageType,
                ["MessageKind"] = message.MessageKind.ToString(),
                ["CorrelationId"] = message.CorrelationId.ToString(),
            },
        };
        if (message.CausationId.HasValue)
            properties.Headers["CausationId"] = message.CausationId.Value.ToString();

        await _poolSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        IChannel? channel = null;
        bool channelHealthy = false;
        try
        {
            // Safe: GetConnectionAsync is TCS-backed (idempotent), CLR reference writes are
            // atomic, so concurrent null-checks with pool size > 1 always assign the same object.
            _connection ??= await _connectionHolder.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

            if (!_channelPool.TryDequeue(out channel) || !channel.IsOpen)
            {
                if (channel is not null)
                    await channel.DisposeAsync().ConfigureAwait(false);
                channel = await _connection
                    .CreateChannelAsync(ConfirmChannelOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            // BasicPublishAsync awaits the broker ack when publisherConfirmationTrackingEnabled
            // is true. A broker nack or a basic.return (mandatory + no binding) throws
            // PublishReturnException here, propagating the failure to the outbox dispatcher
            // which will increment the attempt count rather than marking the row processed.
            await channel.BasicPublishAsync(
                exchange: exchange,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            channelHealthy = true;

            _logger.LogDebug(
                "Forwarded outbox message {MessageId} ({MessageKind}: {MessageType}) to exchange '{Exchange}'.",
                message.Id, message.MessageKind, message.MessageType, exchange);
        }
        finally
        {
            if (channelHealthy && channel is not null && channel.IsOpen)
                _channelPool.Enqueue(channel);
            else if (channel is not null)
                await channel.DisposeAsync().ConfigureAwait(false);

            _poolSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _poolSemaphore.Dispose();
        while (_channelPool.TryDequeue(out IChannel? ch))
            await ch.DisposeAsync().ConfigureAwait(false);
    }
}
