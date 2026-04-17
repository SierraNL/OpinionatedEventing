#nullable enable

using System.Text;
using Microsoft.Extensions.Logging;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.RabbitMQ.Routing;
using RabbitMQ.Client;

namespace OpinionatedEventing.RabbitMQ;

/// <summary>
/// RabbitMQ implementation of <see cref="ITransport"/>.
/// Forwards outbox messages to the correct RabbitMQ exchange (events) or queue (commands).
/// </summary>
internal sealed class RabbitMQTransport : ITransport, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly ILogger<RabbitMQTransport> _logger;

    private IChannel? _publishChannel;
    private readonly SemaphoreSlim _channelLock = new(1, 1);

    /// <summary>Initialises a new <see cref="RabbitMQTransport"/>.</summary>
    public RabbitMQTransport(
        IConnection connection,
        ILogger<RabbitMQTransport> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var type = Type.GetType(message.MessageType)
            ?? throw new InvalidOperationException(
                $"Cannot resolve CLR type '{message.MessageType}' for outbox message {message.Id}.");

        string exchange;
        string routingKey;

        if (message.MessageKind == "Event")
        {
            exchange = MessageNamingConvention.GetExchangeName(type);
            routingKey = string.Empty;
        }
        else
        {
            exchange = MessageNamingConvention.GetQueueName(type);
            routingKey = MessageNamingConvention.GetQueueName(type);
        }

        var body = Encoding.UTF8.GetBytes(message.Payload);
        var properties = new BasicProperties
        {
            MessageId = message.Id.ToString(),
            ContentType = "application/json",
            CorrelationId = message.CorrelationId.ToString(),
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?>
            {
                ["MessageType"] = message.MessageType,
                ["MessageKind"] = message.MessageKind,
                ["CorrelationId"] = message.CorrelationId.ToString(),
            },
        };
        if (message.CausationId.HasValue)
            properties.Headers["CausationId"] = message.CausationId.Value.ToString();

        await _channelLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_publishChannel is null || !_publishChannel.IsOpen)
                _publishChannel = await _connection
                    .CreateChannelAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            await _publishChannel.BasicPublishAsync(
                exchange: exchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _channelLock.Release();
        }

        _logger.LogDebug(
            "Forwarded outbox message {MessageId} ({MessageKind}: {MessageType}) to exchange '{Exchange}'.",
            message.Id, message.MessageKind, message.MessageType, exchange);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _channelLock.Dispose();
        if (_publishChannel is not null)
            await _publishChannel.DisposeAsync().ConfigureAwait(false);
    }
}
