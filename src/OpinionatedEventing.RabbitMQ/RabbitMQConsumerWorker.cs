#nullable enable

using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.RabbitMQ.DependencyInjection;
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
    private readonly ServiceCollectionAccessor _accessor;
    private readonly IOptions<RabbitMQOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RabbitMQConsumerWorker> _logger;

    private readonly List<IChannel> _consumerChannels = new();

    /// <summary>Initialises a new <see cref="RabbitMQConsumerWorker"/>.</summary>
    public RabbitMQConsumerWorker(
        IConnection connection,
        IMessageHandlerRunner handlerRunner,
        IServiceScopeFactory scopeFactory,
        ServiceCollectionAccessor accessor,
        IOptions<RabbitMQOptions> options,
        TimeProvider timeProvider,
        ILogger<RabbitMQConsumerWorker> logger)
    {
        _connection = connection;
        _handlerRunner = handlerRunner;
        _scopeFactory = scopeFactory;
        _accessor = accessor;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        var eventTypes = ScanHandlerTypes(typeof(IEventHandler<>));
        var commandTypes = ScanHandlerTypes(typeof(ICommandHandler<>));

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

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }

        await CloseConsumerChannelsAsync().ConfigureAwait(false);
    }

    private async Task StartConsumerAsync(
        string queueName,
        CancellationToken ct)
    {
        var channel = await _connection.CreateChannelAsync(cancellationToken: ct).ConfigureAwait(false);
        _consumerChannels.Add(channel);

        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: _options.Value.PrefetchCount,
            global: false,
            cancellationToken: ct).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => ProcessDeliveryAsync(channel, ea, ct);

        await channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: ct).ConfigureAwait(false);
    }

    private async Task ProcessDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs ea,
        CancellationToken ct)
    {
        try
        {
            var messageType = GetHeader(ea.BasicProperties, "MessageType");
            var messageKind = GetHeader(ea.BasicProperties, "MessageKind");
            var correlationIdStr = GetHeader(ea.BasicProperties, "CorrelationId");
            var causationIdStr = GetHeader(ea.BasicProperties, "CausationId");

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

            Guid? causationId = Guid.TryParse(causationIdStr, out var c) ? c : null;
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

            // Use CancellationToken.None for both the dead-letter write and the nack so that a
            // host shutdown racing with handler failure does not silently drop the record or leave
            // the message unacknowledged (causing redelivery into the next test or consumer).
            await WriteDeadLetterRecordAsync(ea, ex.Message, CancellationToken.None).ConfigureAwait(false);

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

    private async Task WriteDeadLetterRecordAsync(
        BasicDeliverEventArgs ea,
        string error,
        CancellationToken ct)
    {
        try
        {
            var messageType = GetHeader(ea.BasicProperties, "MessageType") ?? string.Empty;
            var messageKind = GetHeader(ea.BasicProperties, "MessageKind") ?? string.Empty;
            var correlationIdStr = GetHeader(ea.BasicProperties, "CorrelationId");
            var causationIdStr = GetHeader(ea.BasicProperties, "CausationId");

            _ = Guid.TryParse(correlationIdStr, out var correlationId);
            Guid? causationId = Guid.TryParse(causationIdStr, out var c) ? c : null;

            var record = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                MessageType = messageType,
                MessageKind = messageKind,
                Payload = Encoding.UTF8.GetString(ea.Body.Span),
                CorrelationId = correlationId,
                CausationId = causationId,
                CreatedAt = _timeProvider.GetUtcNow(),
                AttemptCount = 1,
            };

            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            await store.SaveAsync(record, ct).ConfigureAwait(false);
            await store.MarkFailedAsync(record.Id, error, ct).ConfigureAwait(false);

            _logger.LogWarning(
                "Dead-lettered message recorded in outbox as {RecordId}.", record.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write dead-letter record to outbox.");
        }
    }

    private async Task CloseConsumerChannelsAsync()
    {
        foreach (var channel in _consumerChannels)
        {
            try
            {
                await channel.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing consumer channel.");
            }
            await channel.DisposeAsync().ConfigureAwait(false);
        }
        _consumerChannels.Clear();
    }

    private List<Type> ScanHandlerTypes(Type openGenericInterface)
        => _accessor.Services
            .Where(d => d.ServiceType.IsGenericType
                && d.ServiceType.GetGenericTypeDefinition() == openGenericInterface)
            .Select(d => d.ServiceType.GetGenericArguments()[0])
            .Distinct()
            .ToList();

    private static string? GetHeader(IReadOnlyBasicProperties properties, string key)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(key, out var value))
            return null;
        return value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value?.ToString();
    }
}
