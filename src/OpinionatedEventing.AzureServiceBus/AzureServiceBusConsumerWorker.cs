#nullable enable

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpinionatedEventing;
using OpinionatedEventing.AzureServiceBus.Attributes;
using OpinionatedEventing.AzureServiceBus.DependencyInjection;
using OpinionatedEventing.AzureServiceBus.Routing;
using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.AzureServiceBus;

/// <summary>
/// Background service that receives messages from Azure Service Bus topics (events) and queues
/// (commands), then dispatches them to registered handlers via <see cref="IMessageHandlerRunner"/>.
/// </summary>
internal sealed class AzureServiceBusConsumerWorker : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly IMessageHandlerRunner _handlerRunner;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceCollectionAccessor _accessor;
    private readonly IOptions<AzureServiceBusOptions> _options;
    private readonly IConsumerPauseController _pauseController;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AzureServiceBusConsumerWorker> _logger;

    private readonly List<ServiceBusProcessor> _processors = new();
    private readonly List<ServiceBusSessionProcessor> _sessionProcessors = new();

    /// <summary>Initialises a new <see cref="AzureServiceBusConsumerWorker"/>.</summary>
    public AzureServiceBusConsumerWorker(
        ServiceBusClient client,
        IMessageHandlerRunner handlerRunner,
        IServiceScopeFactory scopeFactory,
        ServiceCollectionAccessor accessor,
        IOptions<AzureServiceBusOptions> options,
        IConsumerPauseController pauseController,
        TimeProvider timeProvider,
        ILogger<AzureServiceBusConsumerWorker> logger)
    {
        _client = client;
        _handlerRunner = handlerRunner;
        _scopeFactory = scopeFactory;
        _accessor = accessor;
        _options = options;
        _pauseController = pauseController;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        var processorOptions = new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = opts.MaxConcurrentCalls,
        };

        var eventTypes = ScanHandlerTypes(typeof(IEventHandler<>));
        var commandTypes = ScanHandlerTypes(typeof(ICommandHandler<>));

        foreach (var eventType in eventTypes)
        {
            if (string.IsNullOrEmpty(opts.ServiceName))
            {
                _logger.LogWarning(
                    "ServiceName is not configured — skipping subscription for event '{EventType}'.",
                    eventType.Name);
                continue;
            }

            var topicName = MessageNamingConvention.GetTopicName(eventType);
            var processor = _client.CreateProcessor(topicName, opts.ServiceName, processorOptions);
            processor.ProcessMessageAsync += OnMessageAsync;
            processor.ProcessErrorAsync += OnErrorAsync;
            _processors.Add(processor);
            await processor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Consuming event '{EventType}' from topic '{Topic}' subscription '{Subscription}'.",
                eventType.Name, topicName, opts.ServiceName);
        }

        foreach (var commandType in commandTypes)
        {
            var queueName = MessageNamingConvention.GetQueueName(commandType);
            var useSession = opts.EnableSessions
                && Attribute.IsDefined(commandType, typeof(SessionEnabledAttribute));

            if (useSession)
            {
                var sessionOpts = new ServiceBusSessionProcessorOptions
                {
                    AutoCompleteMessages = false,
                    MaxConcurrentSessions = opts.MaxConcurrentSessions,
                };
                var sessionProcessor = _client.CreateSessionProcessor(queueName, sessionOpts);
                sessionProcessor.ProcessMessageAsync += OnSessionMessageAsync;
                sessionProcessor.ProcessErrorAsync += OnErrorAsync;
                _sessionProcessors.Add(sessionProcessor);
                await sessionProcessor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "Consuming command '{CommandType}' from session-enabled queue '{Queue}'.",
                    commandType.Name, queueName);
            }
            else
            {
                var processor = _client.CreateProcessor(queueName, processorOptions);
                processor.ProcessMessageAsync += OnMessageAsync;
                processor.ProcessErrorAsync += OnErrorAsync;
                _processors.Add(processor);
                await processor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "Consuming command '{CommandType}' from queue '{Queue}'.",
                    commandType.Name, queueName);
            }
        }

        await RunPauseLoopAsync(stoppingToken).ConfigureAwait(false);

        await StopAllProcessorsAsync().ConfigureAwait(false);
    }

    private async Task RunPauseLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_pauseController.IsPaused)
            {
                await PauseAllProcessorsAsync().ConfigureAwait(false);
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
                    await ResumeAllProcessorsAsync(stoppingToken).ConfigureAwait(false);
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

    private async Task PauseAllProcessorsAsync()
    {
        foreach (var processor in _processors)
        {
            try { await processor.StopProcessingAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error pausing processor."); }
        }
        foreach (var processor in _sessionProcessors)
        {
            try { await processor.StopProcessingAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error pausing session processor."); }
        }
    }

    private async Task ResumeAllProcessorsAsync(CancellationToken ct)
    {
        foreach (var processor in _processors)
        {
            try { await processor.StartProcessingAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error resuming processor."); }
        }
        foreach (var processor in _sessionProcessors)
        {
            try { await processor.StartProcessingAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error resuming session processor."); }
        }
    }

    private async Task StopAllProcessorsAsync()
    {
        foreach (var processor in _processors)
        {
            try { await processor.StopProcessingAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error stopping processor."); }
            await processor.DisposeAsync().ConfigureAwait(false);
        }
        foreach (var processor in _sessionProcessors)
        {
            try { await processor.StopProcessingAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error stopping session processor."); }
            await processor.DisposeAsync().ConfigureAwait(false);
        }
    }

    private Task OnMessageAsync(ProcessMessageEventArgs args)
        => ProcessReceivedMessageAsync(
            args.Message,
            ct => args.CompleteMessageAsync(args.Message, ct),
            ct => args.AbandonMessageAsync(args.Message, cancellationToken: ct),
            (reason, desc, ct) => args.DeadLetterMessageAsync(args.Message, reason, desc, ct),
            args.CancellationToken);

    private Task OnSessionMessageAsync(ProcessSessionMessageEventArgs args)
        => ProcessReceivedMessageAsync(
            args.Message,
            ct => args.CompleteMessageAsync(args.Message, ct),
            ct => args.AbandonMessageAsync(args.Message, cancellationToken: ct),
            (reason, desc, ct) => args.DeadLetterMessageAsync(args.Message, reason, desc, ct),
            args.CancellationToken);

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "Azure Service Bus processor error. Source={Source}, EntityPath={EntityPath}.",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }


    private async Task ProcessReceivedMessageAsync(
        ServiceBusReceivedMessage message,
        Func<CancellationToken, Task> complete,
        Func<CancellationToken, Task> abandon,
        Func<string, string, CancellationToken, Task> deadLetter,
        CancellationToken ct)
    {
        try
        {
            var messageType = message.ApplicationProperties.TryGetValue("MessageType", out var mt)
                ? mt as string : null;
            var messageKind = message.ApplicationProperties.TryGetValue("MessageKind", out var mk)
                ? mk as string : null;
            var correlationIdStr = message.ApplicationProperties.TryGetValue("CorrelationId", out var cid)
                ? cid as string : null;
            var causationIdStr = message.ApplicationProperties.TryGetValue("CausationId", out var caus)
                ? caus as string : null;

            if (messageType is null || messageKind is null || correlationIdStr is null)
            {
                _logger.LogWarning(
                    "Received message {MessageId} is missing required application properties; dead-lettering.",
                    message.MessageId);
                await deadLetter("InvalidMessageFormat", "Missing required application properties.", ct)
                    .ConfigureAwait(false);
                return;
            }

            if (!Guid.TryParse(correlationIdStr, out var correlationId))
            {
                await deadLetter("InvalidMessageFormat", "CorrelationId is not a valid Guid.", ct)
                    .ConfigureAwait(false);
                return;
            }

            Guid? causationId = Guid.TryParse(causationIdStr, out var c) ? c : null;
            var payload = message.Body.ToString();

            await _handlerRunner.RunAsync(messageType, messageKind, payload, correlationId, causationId, ct)
                .ConfigureAwait(false);

            await complete(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host is shutting down; let the processor handle the message lock expiry.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to handle message {MessageId} (delivery {DeliveryCount}/{MaxDeliveryCount}).",
                message.MessageId, message.DeliveryCount, _options.Value.MaxDeliveryCount);

            if (message.DeliveryCount >= _options.Value.MaxDeliveryCount)
            {
                await WriteDeadLetterRecordAsync(message, ex.Message, ct).ConfigureAwait(false);
                await deadLetter("HandlerException", ex.Message, ct).ConfigureAwait(false);
            }
            else
            {
                await abandon(ct).ConfigureAwait(false);
            }
        }
    }

    private async Task WriteDeadLetterRecordAsync(
        ServiceBusReceivedMessage message,
        string error,
        CancellationToken ct)
    {
        try
        {
            var messageType = message.ApplicationProperties.TryGetValue("MessageType", out var mt)
                ? mt as string ?? string.Empty : string.Empty;
            var messageKind = message.ApplicationProperties.TryGetValue("MessageKind", out var mk)
                ? mk as string ?? string.Empty : string.Empty;
            var correlationIdStr = message.ApplicationProperties.TryGetValue("CorrelationId", out var cid)
                ? cid as string : null;
            var causationIdStr = message.ApplicationProperties.TryGetValue("CausationId", out var caus)
                ? caus as string : null;

            _ = Guid.TryParse(correlationIdStr, out var correlationId);
            Guid? causationId = Guid.TryParse(causationIdStr, out var c) ? c : null;

            var record = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                MessageType = messageType,
                MessageKind = messageKind,
                Payload = message.Body.ToString(),
                CorrelationId = correlationId,
                CausationId = causationId,
                CreatedAt = _timeProvider.GetUtcNow(),
                AttemptCount = message.DeliveryCount,
            };

            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            await store.SaveAsync(record, ct).ConfigureAwait(false);
            // MarkFailedAsync calls SaveChanges for EF-backed stores, persisting the record.
            await store.MarkFailedAsync(record.Id, error, ct).ConfigureAwait(false);

            _logger.LogWarning(
                "Dead-lettered message {OriginalMessageId} recorded in outbox as {RecordId}.",
                message.MessageId, record.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write dead-letter record for message {MessageId} to outbox.",
                message.MessageId);
        }
    }

    private List<Type> ScanHandlerTypes(Type openGenericInterface)
        => _accessor.Services
            .Where(d => d.ServiceType.IsGenericType
                && d.ServiceType.GetGenericTypeDefinition() == openGenericInterface)
            .Select(d => d.ServiceType.GetGenericArguments()[0])
            .Distinct()
            .ToList();
}
