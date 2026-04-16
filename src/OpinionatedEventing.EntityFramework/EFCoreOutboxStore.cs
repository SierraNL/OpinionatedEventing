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
/// When <see cref="OpinionatedEventing.Options.OutboxOptions.ConcurrentWorkers"/> is greater
/// than <c>1</c>, consider using a database that supports <c>SELECT … FOR UPDATE SKIP LOCKED</c>
/// (SQL Server, PostgreSQL) and configure the <c>GetPendingAsync</c> query accordingly to
/// avoid duplicate dispatch under concurrent workers.
/// </para>
/// </remarks>
internal sealed class EFCoreOutboxStore<TDbContext> : IOutboxStore
    where TDbContext : DbContext
{
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
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null && m.FailedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.Set<OutboxMessage>().FindAsync([id], cancellationToken);
        if (message is null) return;

        message.ProcessedAt = _timeProvider.GetUtcNow();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.Set<OutboxMessage>().FindAsync([id], cancellationToken);
        if (message is null) return;

        message.FailedAt = _timeProvider.GetUtcNow();
        message.Error = error;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task IncrementAttemptAsync(Guid id, string error, CancellationToken cancellationToken = default)
    {
        var message = await _dbContext.Set<OutboxMessage>().FindAsync([id], cancellationToken);
        if (message is null) return;

        message.AttemptCount++;
        message.Error = error;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
