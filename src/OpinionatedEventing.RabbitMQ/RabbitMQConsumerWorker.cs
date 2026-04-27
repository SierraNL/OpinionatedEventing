#nullable enable

using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpinionatedEventing;
using OpinionatedEventing.DependencyInjection;
using OpinionatedEventing.RabbitMQ.Routing;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpinionatedEventing.RabbitMQ;

/// <summary>
/// Background service that consumes messages from RabbitMQ fanout exchanges (events) and direct
/// queues (commands), then dispatches them to registered handlers via
/// <see cref="IMessageHandlerRunner"/>.
/// </summary>
internal sealed class RabbitMQConsumerWorker : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IMessageHandlerRunner _handlerRunner;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MessageHandlerRegistry _registry;
    private readonly IOptions<RabbitMQOptions> _options;
    private readonly IConsumerPauseController _pauseController;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RabbitMQConsumerWorker> _logger;

    private sealed class ConsumerEntry
    {
        public required IChannel Channel { get; init; }
        public required string QueueName { get; init; }
        public string ConsumerTag { get; set; } = string.Empty;
    }

    private readonly List<ConsumerEntry> _consumers = new();

    /// <summary>Initialises a new <see cref="RabbitMQConsumerWorker"/>.</summary>
    public RabbitMQConsumerWorker(
        IConnection connection,
        IMessageHandlerRunner handlerRunner,
        IServiceScopeFactory scopeFactory,
        MessageHandlerRegistry registry,
        IOptions<RabbitMQOptions> options,
        IConsumerPauseController pauseController,
        TimeProvider timeProvider,
        ILogger<RabbitMQConsumerWorker> logger)
    {
        _connection = connection;
        _handlerRunner = handlerRunner;
        _scopeFactory = scopeFactory;
        _registry = registry;
        _options = options;
        _pauseController = pauseController;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        var eventTypes = _registry.EventTypes;
        var commandTypes = _registry.CommandTypes;

        foreach (var eventType in eventTypes)
        {
            if (string.IsNullOrEmpty(opts.ServiceName))
            {
                _logger.LogWarning(
                    "ServiceName is not configured — skipping consumer for event '{EventType}'.",
                    eventType.Name);
                continue;
            }

            var queueName = MessageNamingConvention.GetEventQueueName(eventType, opts.ServiceName);
            var exchangeName = MessageNamingConvention.GetExchangeName(eventType);
            await StartConsumerAsync(queueName, stoppingToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Consuming event '{EventType}' from queue '{Queue}' (exchange '{Exchange}').",
                eventType.Name, queueName, exchangeName);
        }

        foreach (var commandType in commandTypes)
        {
            var queueName = MessageNamingConvention.GetQueueName(commandType);
            await StartConsumerAsync(queueName, stoppingToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Consuming command '{CommandType}' from queue '{Queue}'.",
                commandType.Name, queueName);
        }

        await RunPauseLoopAsync(stoppingToken).ConfigureAwait(false);

        await CloseConsumerChannelsAsync().ConfigureAwait(false);
    }

    private async Task RunPauseLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_pauseController.IsPaused)
            {
                await PauseAllConsumersAsync().ConfigureAwait(false);
                _logger.LogWarning("Broker consumers paused: readiness probe is unhealthy.");

                try
                {
                    await _pauseController.WhenStateChangedAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                if (!stoppingToken.IsCancellationRequested)
                {
                    await ResumeAllConsumersAsync(stoppingToken).ConfigureAwait(false);
                    _logger.LogInformation("Broker consumers resumed: readiness probe recovered.");
                }
            }
            else
            {
                try
                {
                    await _pauseController.WhenStateChangedAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task PauseAllConsumersAsync()
    {
        foreach (var entry in _consumers)
        {
            if (string.IsNullOrEmpty(entry.ConsumerTag))
                continue;
            try
            {
                await entry.Channel.BasicCancelAsync(entry.ConsumerTag).ConfigureAwait(false);
                entry.ConsumerTag = string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error pausing consumer for queue '{Queue}'.", entry.QueueName);
            }
        }
    }

    private async Task ResumeAllConsumersAsync(CancellationToken ct)
    {
        foreach (var entry in _consumers)
        {
            try
            {
                var consumer = new AsyncEventingBasicConsumer(entry.Channel);
                consumer.ReceivedAsync += (_, ea) => ProcessDeliveryAsync(entry.Channel, ea, ct);
                var tag = await entry.Channel.BasicConsumeAsync(
                    queue: entry.QueueName,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: ct).ConfigureAwait(false);
                entry.ConsumerTag = tag;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error resuming consumer for queue '{Queue}'.", entry.QueueName);
            }
        }
    }

    private async Task StartConsumerAsync(
        string queueName,
        CancellationToken ct)
    {
        var channel = await _connection.CreateChannelAsync(cancellationToken: ct).ConfigureAwait(false);

        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: _options.Value.PrefetchCount,
            global: false,
            cancellationToken: ct).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(channel);

        var entry = new ConsumerEntry { Channel = channel, QueueName = queueName };
        _consumers.Add(entry);

        consumer.ReceivedAsync += (_, ea) => ProcessDeliveryAsync(channel, ea, ct);

        var tag = await channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: ct).ConfigureAwait(false);

        entry.ConsumerTag = tag;
    }

    internal async Task ProcessDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs ea,
        CancellationToken ct)
    {
        try
        {
            var messageType = GetHeader(ea.BasicProperties, "MessageType");
            var messageKind = GetHeader(ea.BasicProperties, "MessageKind");
            var correlationIdStr = GetHeader(ea.BasicProperties, "CorrelationId");

            if (messageType is null || messageKind is null || correlationIdStr is null)
            {
                _logger.LogWarning(
                    "Received message is missing required headers; nacking without requeue.");
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }

            if (!Guid.TryParse(correlationIdStr, out var correlationId))
            {
                _logger.LogWarning(
                    "Received message has invalid CorrelationId '{CorrelationId}'; nacking without requeue.",
                    correlationIdStr);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, CancellationToken.None)
                    .ConfigureAwait(false);
                return;
            }

            Guid? causationId = Guid.TryParse(ea.BasicProperties.MessageId, out var c) ? c : null;
            var payload = Encoding.UTF8.GetString(ea.Body.Span);

            await _handlerRunner
                .RunAsync(messageType, messageKind, payload, correlationId, causationId, ct)
                .ConfigureAwait(false);

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle message with delivery tag {DeliveryTag}.", ea.DeliveryTag);

            // CancellationToken.None: host shutdown must not leave the message unacknowledged.
            try
            {
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception nackEx)
            {
                _logger.LogWarning(nackEx,
                    "Failed to nack message with delivery tag {DeliveryTag}; message may be redelivered.",
                    ea.DeliveryTag);
            }
        }
    }

    private async Task CloseConsumerChannelsAsync()
    {
        foreach (var entry in _consumers)
        {
            try
            {
                await entry.Channel.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing consumer channel.");
            }
            await entry.Channel.DisposeAsync().ConfigureAwait(false);
        }
        _consumers.Clear();
    }

    private static string? GetHeader(IReadOnlyBasicProperties properties, string key)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(key, out var value))
            return null;
        return value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value?.ToString();
    }
}
