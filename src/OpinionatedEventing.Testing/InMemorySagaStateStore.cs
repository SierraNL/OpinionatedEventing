#nullable enable

using System.Collections.Concurrent;
using OpinionatedEventing.Sagas;

namespace OpinionatedEventing.Testing;

/// <summary>
/// In-memory implementation of <see cref="ISagaStateStore"/> for use in unit tests.
/// Thread-safe. Not for production use.
/// </summary>
public sealed class InMemorySagaStateStore : ISagaStateStore
{
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<(string SagaType, string CorrelationId), SagaState> _states = new();
    private readonly object _claimLock = new();

    /// <summary>Gets a snapshot of all saga states currently in the store.</summary>
    public IReadOnlyList<SagaState> States => _states.Values.ToList();

    /// <inheritdoc/>
    public Task<SagaState?> FindAsync(
        string sagaType,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _states.TryGetValue((sagaType, correlationId), out var state);
        return Task.FromResult(state);
    }

    /// <inheritdoc/>
    public Task SaveAsync(SagaState state, CancellationToken cancellationToken = default)
    {
        _states[(state.SagaType, state.CorrelationId)] = state;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateAsync(SagaState state, CancellationToken cancellationToken = default)
    {
        _states[(state.SagaType, state.CorrelationId)] = state;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Atomically claims expired sagas using an in-process lock, preventing two concurrent
    /// callers on the same store instance from processing the same saga twice. Not a substitute
    /// for the database-level claiming in <c>EFCoreSagaStateStore</c> — use that in production.
    /// </remarks>
    public Task<IReadOnlyList<SagaState>> GetExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var lockUntil = now.Add(LockDuration);
        var claimToken = Guid.NewGuid().ToString();
        var claimed = new List<SagaState>();

        lock (_claimLock)
        {
            foreach (var state in _states.Values)
            {
                if (state.Status == SagaStatus.Active
                    && state.ExpiresAt.HasValue
                    && state.ExpiresAt.Value <= now
                    && (state.LockedUntil == null || state.LockedUntil < now))
                {
                    state.LockedBy = claimToken;
                    state.LockedUntil = lockUntil;
                    claimed.Add(state);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<SagaState>>(claimed);
    }
}
