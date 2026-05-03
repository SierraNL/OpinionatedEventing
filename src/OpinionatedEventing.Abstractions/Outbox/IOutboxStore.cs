namespace OpinionatedEventing.Outbox;

/// <summary>
/// Persistence contract for the outbox.
/// The default implementation is provided by <c>OpinionatedEventing.EntityFramework</c>.
/// A test-only in-memory implementation is available in <c>OpinionatedEventing.Testing</c>.
/// </summary>
public interface IOutboxStore
{
    /// <summary>Persists a new <see cref="OutboxMessage"/> to the outbox.</summary>
    /// <param name="message">The message to save.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task SaveAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the next batch of pending (unprocessed, non-dead-lettered) messages.
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to return.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <remarks>
    /// Implementations must ensure that concurrent calls do not return overlapping sets of messages.
    /// Use pessimistic row-level locking (e.g. <c>SELECT … FOR UPDATE SKIP LOCKED</c>) when the
    /// store is backed by a relational database and
    /// <c>OutboxOptions.ConcurrentWorkers</c> is greater than <c>1</c>.
    /// </remarks>
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the outbox entry with the given <paramref name="id"/> as successfully processed.
    /// Sets <see cref="OutboxMessage.ProcessedAt"/> to the current UTC time.
    /// </summary>
    /// <param name="id">The identifier of the outbox message.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the outbox entry with the given <paramref name="id"/> as permanently failed (dead-lettered).
    /// Sets <see cref="OutboxMessage.FailedAt"/> to the current UTC time and records the <paramref name="error"/>.
    /// </summary>
    /// <param name="id">The identifier of the outbox message.</param>
    /// <param name="error">A description of the error that caused the failure.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a transient dispatch failure without dead-lettering the message.
    /// Increments <see cref="OutboxMessage.AttemptCount"/> by one, stores the last error description,
    /// and sets <see cref="OutboxMessage.NextAttemptAt"/> to defer the next retry.
    /// The message is not eligible for re-dispatch until <paramref name="nextAttemptAt"/>.
    /// </summary>
    /// <param name="id">The identifier of the outbox message.</param>
    /// <param name="error">A description of the error from the failed attempt.</param>
    /// <param name="nextAttemptAt">
    /// The earliest UTC time at which this message should be retried.
    /// Pass <see langword="null"/> to make the message immediately eligible.
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task IncrementAttemptAsync(Guid id, string error, DateTimeOffset? nextAttemptAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes processed outbox messages whose <see cref="OutboxMessage.ProcessedAt"/>
    /// is earlier than <paramref name="cutoff"/>.
    /// </summary>
    /// <param name="cutoff">Rows with <c>ProcessedAt &lt; cutoff</c> are deleted.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The number of rows deleted.</returns>
    Task<int> DeleteProcessedAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes dead-lettered outbox messages whose <see cref="OutboxMessage.FailedAt"/>
    /// is earlier than <paramref name="cutoff"/>.
    /// </summary>
    /// <param name="cutoff">Rows with <c>FailedAt &lt; cutoff</c> are deleted.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The number of rows deleted.</returns>
    Task<int> DeleteFailedAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
