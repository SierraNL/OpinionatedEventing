using Microsoft.EntityFrameworkCore;
using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.EntityFramework;

/// <summary>
/// EF Core implementation of <see cref="IOutboxStore"/>.
/// Persists outbox messages in the same <typeparamref name="TDbContext"/> as the calling unit of work,
/// ensuring atomic writes when <c>SaveChanges</c> is called by the application.
/// </summary>
/// <typeparam name="TDbContext">The application's <see cref="DbContext"/> type.</typeparam>
/// <remarks>
/// <para>
/// <see cref="SaveAsync"/> only stages the message in the change tracker — it does not call
/// <c>SaveChanges</c>. The surrounding business transaction is responsible for committing.
/// </para>
/// <para>
/// <see cref="GetPendingAsync"/>, <see cref="MarkProcessedAsync"/>, <see cref="MarkFailedAsync"/>,
/// and <see cref="IncrementAttemptAsync"/> each call <c>SaveChangesAsync</c> internally because
/// they are invoked by <c>OutboxDispatcherWorker</c> in an isolated scope.
/// </para>
/// <para>
/// <see cref="GetPendingAsync"/> uses a <em>claim-column</em> approach to prevent competing
/// consumers from dispatching the same message twice. Each call atomically stamps a batch with a
/// unique <c>LockedBy</c> token and a <c>LockedUntil</c> expiry timestamp before returning it.
/// A two-step SELECT-then-UPDATE is used: the first SELECT identifies candidate IDs; the UPDATE
/// re-checks the lock condition so that only rows not yet claimed by a concurrent caller are
/// stamped. This is safe on any EF Core relational provider because a SQL UPDATE is atomic at the
/// row level — two concurrent UPDATEs for the same row are serialised by the database engine.
/// Rows whose <c>LockedUntil</c> has expired are automatically re-eligible, providing crash
/// recovery without manual intervention.
/// </para>
/// </remarks>
internal sealed class EFCoreOutboxStore<TDbContext> : IOutboxStore
    where TDbContext : DbContext
{
    /// <summary>How long a claimed batch is held before the lock is considered expired.</summary>
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private readonly TDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initialises a new <see cref="EFCoreOutboxStore{TDbContext}"/>.</summary>
    public EFCoreOutboxStore(TDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Stages the message in the EF change tracker without calling <c>SaveChanges</c>.
    /// The caller must include a <c>SaveChangesAsync</c> call within the same transaction.
    /// </remarks>
    public Task SaveAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<OutboxMessage>().Add(message);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Atomically claims a batch using the claim-column approach: candidate rows are identified
    /// by a SELECT, then stamped with a unique <c>LockedBy</c> token via an UPDATE that re-checks
    /// the lock predicate. Only rows successfully stamped with this call's token are returned,
    /// preventing any other concurrent caller from receiving the same messages.
    /// </remarks>
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset lockUntil = now.Add(LockDuration);
        string claimToken = Guid.NewGuid().ToString();

        // Step 1: identify candidates — rows that are pending, not currently claimed, and past their backoff delay.
        List<Guid> ids = await _dbContext.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null && m.FailedAt == null &&
                        (m.LockedUntil == null || m.LockedUntil < now) &&
                        (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return [];

        // Step 2: atomically claim — the re-check of the lock predicate in the WHERE clause
        // ensures that a row already claimed by a concurrent caller is not double-claimed.
        // Two concurrent UPDATEs for the same row are serialised by the database engine, so
        // whichever runs second will find the lock condition already false and skip that row.
        await _dbContext.Set<OutboxMessage>()
            .Where(m => ids.Contains(m.Id) &&
                        (m.LockedUntil == null || m.LockedUntil < now))
            .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.LockedUntil, lockUntil)
                    .SetProperty(m => m.LockedBy, claimToken),
                cancellationToken);

        // Step 3: return exactly the rows this instance claimed, ordered for deterministic dispatch.
        // Filtering on LockedBy alone is sufficient — the GUID is unique per call.
        return await _dbContext.Set<OutboxMessage>()
            .Where(m => m.LockedBy == claimToken)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        await _dbContext.Set<OutboxMessage>()
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, now), cancellationToken);
    }

    /// <inheritdoc/>
    public async Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        await _dbContext.Set<OutboxMessage>()
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.FailedAt, now)
                .SetProperty(m => m.Error, error),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task IncrementAttemptAsync(Guid id, string error, DateTimeOffset? nextAttemptAt, CancellationToken cancellationToken = default)
    {
        await _dbContext.Set<OutboxMessage>()
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.AttemptCount, m => m.AttemptCount + 1)
                .SetProperty(m => m.Error, error)
                .SetProperty(m => m.NextAttemptAt, nextAttemptAt)
                // Clear the claim so the message is re-eligible after the backoff delay.
                .SetProperty(m => m.LockedBy, (string?)null)
                .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteProcessedAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        => await _dbContext.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<int> DeleteFailedAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        => await _dbContext.Set<OutboxMessage>()
            .Where(m => m.FailedAt != null && m.FailedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
}
