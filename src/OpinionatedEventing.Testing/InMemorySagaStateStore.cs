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
    private readonly ConcurrentDictionary<(string SagaType, string CorrelationId), SagaState> _states = new();

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
    public Task<IReadOnlyList<SagaState>> GetExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var expired = _states.Values
            .Where(s => s.Status == SagaStatus.Active
                && s.ExpiresAt.HasValue
                && s.ExpiresAt.Value <= now)
            .ToList();

        return Task.FromResult<IReadOnlyList<SagaState>>(expired);
    }
}
