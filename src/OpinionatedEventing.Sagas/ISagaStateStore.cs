namespace OpinionatedEventing.Sagas;

/// <summary>
/// Persistence contract for saga state.
/// The default implementation is provided by <c>OpinionatedEventing.EntityFramework</c>.
/// </summary>
public interface ISagaStateStore
{
    /// <summary>
    /// Returns the saga instance for the given orchestrator type and correlation identifier,
    /// or <see langword="null"/> if no matching instance exists.
    /// </summary>
    /// <param name="sagaType">The assembly-qualified CLR type name of the orchestrator.</param>
    /// <param name="correlationId">The correlation identifier of the saga instance.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task<SagaState?> FindAsync(
        string sagaType,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a new saga state entry.</summary>
    /// <param name="state">The saga state to save.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task SaveAsync(SagaState state, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing saga state entry.</summary>
    /// <param name="state">The saga state to update.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task UpdateAsync(SagaState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all <see cref="SagaStatus.Active"/> saga instances whose
    /// <see cref="SagaState.ExpiresAt"/> is less than or equal to <paramref name="now"/>.
    /// </summary>
    /// <param name="now">The current point in time to compare against <see cref="SagaState.ExpiresAt"/>.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task<IReadOnlyList<SagaState>> GetExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
