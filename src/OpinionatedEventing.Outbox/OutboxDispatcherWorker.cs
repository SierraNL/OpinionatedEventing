#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Options;

namespace OpinionatedEventing.Outbox;

/// <summary>
/// Background service that polls <see cref="IOutboxStore"/> for pending messages and forwards
/// them to the registered <see cref="ITransport"/>. Supports configurable concurrency, batch size,
/// poll interval, and dead-letter handling via <see cref="OutboxOptions"/>.
/// </summary>
public sealed class OutboxDispatcherWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OpinionatedEventingOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxDispatcherWorker> _logger;

    /// <summary>Initialises a new <see cref="OutboxDispatcherWorker"/>.</summary>
    public OutboxDispatcherWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<OpinionatedEventingOptions> options,
        TimeProvider timeProvider,
        ILogger<OutboxDispatcherWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerCount = Math.Max(1, _options.Value.Outbox.ConcurrentWorkers);
        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => RunWorkerLoopAsync(stoppingToken))
            .ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private async Task RunWorkerLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in outbox dispatcher worker.");
            }

            await Task.Delay(_options.Value.Outbox.PollInterval, _timeProvider, stoppingToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task DispatchBatchAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var transport = scope.ServiceProvider.GetRequiredService<ITransport>();
        var outboxOptions = _options.Value.Outbox;

        var messages = await store.GetPendingAsync(outboxOptions.BatchSize, stoppingToken).ConfigureAwait(false);

        foreach (var message in messages)
        {
            if (stoppingToken.IsCancellationRequested)
                return;

            try
            {
                await transport.SendAsync(message, stoppingToken).ConfigureAwait(false);
                await store.MarkProcessedAsync(message.Id, stoppingToken).ConfigureAwait(false);
                _logger.LogDebug(
                    "Dispatched outbox message {MessageId} ({MessageType}).",
                    message.Id, message.MessageType);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                var nextAttempt = message.AttemptCount + 1;

                _logger.LogWarning(ex,
                    "Failed to dispatch outbox message {MessageId} (attempt {Attempt}/{MaxAttempts}).",
                    message.Id, nextAttempt, outboxOptions.MaxAttempts);

                if (nextAttempt >= outboxOptions.MaxAttempts)
                {
                    await store.MarkFailedAsync(message.Id, ex.Message, stoppingToken).ConfigureAwait(false);
                    _logger.LogError(
                        "Outbox message {MessageId} dead-lettered after {MaxAttempts} failed attempts.",
                        message.Id, outboxOptions.MaxAttempts);
                }
                else
                {
                    await store.IncrementAttemptAsync(message.Id, ex.Message, stoppingToken).ConfigureAwait(false);
                }
            }
        }
    }
}
