#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Options;

namespace OpinionatedEventing.Outbox;

/// <summary>
/// Background service that periodically deletes outbox rows that have exceeded their
/// configured retention window. Processed rows are governed by
/// <see cref="OutboxOptions.ProcessedRetention"/>; dead-lettered rows by
/// <see cref="OutboxOptions.FailedRetention"/>. A <see langword="null"/> retention value means
/// those rows are never deleted automatically.
/// The first sweep runs after one full <see cref="OutboxOptions.CleanupInterval"/> (default: 1 hour).
/// </summary>
/// <remarks>
/// <para>
/// Cleanup deletes are safe to run concurrently across multiple application instances.
/// The delete predicates (<c>ProcessedAt &lt; cutoff</c> / <c>FailedAt &lt; cutoff</c>) are
/// mutually exclusive with the dispatcher's pending-message predicate
/// (<c>ProcessedAt IS NULL AND FailedAt IS NULL</c>), so cleanup can never remove a row
/// that the dispatcher is currently working on. Concurrent cleanup runs on the same rows
/// are idempotent — the second delete simply affects zero rows.
/// </para>
/// </remarks>
public sealed class OutboxCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OpinionatedEventingOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxCleanupWorker> _logger;

    /// <summary>Initialises a new <see cref="OutboxCleanupWorker"/>.</summary>
    public OutboxCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<OpinionatedEventingOptions> options,
        TimeProvider timeProvider,
        ILogger<OutboxCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_options.Value.Outbox.CleanupInterval, _timeProvider, stoppingToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (stoppingToken.IsCancellationRequested)
                return;

            try
            {
                await RunCleanupAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in outbox cleanup worker.");
            }
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        OutboxOptions outboxOptions = _options.Value.Outbox;
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IOutboxStore store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();

        if (outboxOptions.ProcessedRetention is { } processedRetention)
        {
            DateTimeOffset processedCutoff = now - processedRetention;
            int deleted = await store.DeleteProcessedAsync(processedCutoff, cancellationToken).ConfigureAwait(false);
            if (deleted > 0)
                _logger.LogInformation("Deleted {Count} processed outbox messages older than {Cutoff:O}.", deleted, processedCutoff);
        }

        if (outboxOptions.FailedRetention is { } failedRetention)
        {
            DateTimeOffset failedCutoff = now - failedRetention;
            int deleted = await store.DeleteFailedAsync(failedCutoff, cancellationToken).ConfigureAwait(false);
            if (deleted > 0)
                _logger.LogInformation("Deleted {Count} dead-lettered outbox messages older than {Cutoff:O}.", deleted, failedCutoff);
        }
    }
}
