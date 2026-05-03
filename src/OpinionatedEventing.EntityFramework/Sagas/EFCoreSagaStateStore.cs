using Microsoft.EntityFrameworkCore;
using OpinionatedEventing.Sagas;

namespace OpinionatedEventing.EntityFramework.Sagas;

/// <summary>
/// EF Core implementation of <see cref="ISagaStateStore"/>.
/// Persists saga state in the same <typeparamref name="TDbContext"/> as the application.
/// </summary>
/// <typeparam name="TDbContext">The application's <see cref="DbContext"/> type.</typeparam>
/// <remarks>
/// <para>
/// <see cref="GetExpiredAsync"/> uses a <em>claim-column</em> approach to prevent competing
/// timeout workers from processing the same expired saga twice. Each call atomically stamps
/// matching rows with a unique <c>LockedBy</c> token and a <c>LockedUntil</c> expiry before
/// returning them. A two-step SELECT-then-UPDATE is used: the first SELECT identifies candidate
/// IDs; the UPDATE re-checks the lock condition so that only rows not yet claimed by a concurrent
/// caller are stamped. This is safe on any EF Core relational provider because a SQL UPDATE is
/// atomic at the row level. Rows whose <c>LockedUntil</c> has expired are automatically
/// re-eligible, providing crash recovery without manual intervention.
/// </para>
/// </remarks>
internal sealed class EFCoreSagaStateStore<TDbContext> : ISagaStateStore
    where TDbContext : DbContext
{
    /// <summary>How long a claimed saga is held before the lock is considered expired.</summary>
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private readonly TDbContext _dbContext;

    /// <summary>Initialises a new <see cref="EFCoreSagaStateStore{TDbContext}"/>.</summary>
    public EFCoreSagaStateStore(TDbContext dbContext)
        => _dbContext = dbContext;

    /// <inheritdoc/>
    public async Task<SagaState?> FindAsync(
        string sagaType,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<SagaState>()
            .FirstOrDefaultAsync(
                s => s.SagaType == sagaType && s.CorrelationId == correlationId,
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(SagaState state, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<SagaState>().Add(state);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(SagaState state, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<SagaState>().Update(state);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// On relational providers, atomically claims expired sagas using the claim-column approach:
    /// candidate rows are identified by a SELECT, then stamped with a unique <c>LockedBy</c>
    /// token via an UPDATE that re-checks the lock predicate. Only rows successfully stamped with
    /// this call's token are returned, preventing any other concurrent caller from processing the
    /// same sagas. On non-relational providers (e.g. EF InMemory used in unit tests),
    /// <c>ExecuteUpdateAsync</c> is not available; the method falls back to a simple read with
    /// no locking, which is safe in single-instance scenarios.
    /// </remarks>
    public async Task<IReadOnlyList<SagaState>> GetExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await _dbContext.Set<SagaState>()
                .Where(s => s.Status == SagaStatus.Active
                         && s.ExpiresAt.HasValue
                         && s.ExpiresAt <= now)
                .ToListAsync(cancellationToken);
        }

        DateTimeOffset lockUntil = now.Add(LockDuration);
        string claimToken = Guid.NewGuid().ToString();

        // Step 1: identify candidates — active expired sagas not currently claimed.
        List<Guid> ids = await _dbContext.Set<SagaState>()
            .Where(s => s.Status == SagaStatus.Active
                     && s.ExpiresAt.HasValue
                     && s.ExpiresAt <= now
                     && (s.LockedUntil == null || s.LockedUntil < now))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return [];

        // Step 2: atomically claim — the re-check of the lock predicate in the WHERE clause
        // ensures that a row already claimed by a concurrent caller is not double-claimed.
        await _dbContext.Set<SagaState>()
            .Where(s => ids.Contains(s.Id) &&
                        (s.LockedUntil == null || s.LockedUntil < now))
            .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.LockedUntil, lockUntil)
                    .SetProperty(m => m.LockedBy, claimToken),
                cancellationToken);

        // Step 3: return exactly the rows this instance claimed.
        return await _dbContext.Set<SagaState>()
            .Where(s => s.LockedBy == claimToken)
            .ToListAsync(cancellationToken);
    }
}
