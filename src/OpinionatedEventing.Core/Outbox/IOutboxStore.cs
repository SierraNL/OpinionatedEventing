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
}
