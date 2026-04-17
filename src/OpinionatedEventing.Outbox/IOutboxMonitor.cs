#nullable enable

namespace OpinionatedEventing.Outbox;

/// <summary>
/// Optional service that exposes outbox health metrics.
/// Register an implementation to surface outbox counts in health checks and metrics dashboards.
/// </summary>
public interface IOutboxMonitor
{
    /// <summary>Returns the current count of pending (unprocessed, non-dead-lettered) outbox messages.</summary>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the current count of dead-lettered (permanently failed) outbox messages.</summary>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task<int> GetDeadLetterCountAsync(CancellationToken cancellationToken = default);
}
