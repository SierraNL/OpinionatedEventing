namespace OpinionatedEventing.Options;

/// <summary>
/// Configuration options for the outbox dispatcher and cleanup worker.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>
    /// Gets or sets how often <c>OutboxDispatcherWorker</c> polls for pending messages.
    /// Defaults to <c>1 second</c>.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum number of messages returned in a single poll cycle.
    /// Defaults to <c>50</c>.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Gets or sets the maximum number of dispatch attempts before a message is dead-lettered.
    /// Defaults to <c>5</c>.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the number of concurrent dispatch workers.
    /// Defaults to <c>1</c>.
    /// </summary>
    /// <remarks>
    /// When set above <c>1</c>, each worker races to claim a batch via the claim-column mechanism in
    /// <c>EFCoreOutboxStore.GetPendingAsync</c>: a unique token is written atomically to
    /// <c>LockedBy</c> / <c>LockedUntil</c> before the batch is returned, so concurrent workers never
    /// receive the same messages. The in-memory test store (<c>InMemoryOutboxStore</c>) does
    /// <em>not</em> enforce this and must not be used with <c>ConcurrentWorkers &gt; 1</c> in tests
    /// that verify dispatch ordering.
    /// </remarks>
    public int ConcurrentWorkers { get; set; } = 1;

    /// <summary>
    /// Gets or sets the cap on the exponential retry backoff delay applied between dispatch attempts.
    /// The delay for attempt <em>n</em> is <c>min(2^n seconds, MaxRetryDelay)</c>.
    /// Defaults to <c>5 minutes</c>.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets how long successfully processed messages are retained before the
    /// <c>OutboxCleanupWorker</c> deletes them.
    /// Defaults to <c>7 days</c>.
    /// Set to <see langword="null"/> to disable deletion of processed messages.
    /// </summary>
    public TimeSpan? ProcessedRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets how long dead-lettered messages are retained before the
    /// <c>OutboxCleanupWorker</c> deletes them.
    /// Defaults to <see langword="null"/> (dead-letters are kept indefinitely).
    /// </summary>
    public TimeSpan? FailedRetention { get; set; } = null;

    /// <summary>
    /// Gets or sets how often the <c>OutboxCleanupWorker</c> runs its retention sweep.
    /// Defaults to <c>1 hour</c>.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
