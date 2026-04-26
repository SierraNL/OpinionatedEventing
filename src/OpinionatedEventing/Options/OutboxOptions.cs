namespace OpinionatedEventing.Options;

/// <summary>
/// Configuration options for the outbox dispatcher.
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
}
