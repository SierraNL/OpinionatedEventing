using Microsoft.EntityFrameworkCore;
using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.EntityFramework;

/// <summary>
/// EF Core implementation of <see cref="IOutboxMonitor"/>.
/// Queries the <c>outbox_messages</c> table directly to return live pending and dead-letter counts.
/// </summary>
/// <typeparam name="TDbContext">The application's <see cref="DbContext"/> type.</typeparam>
internal sealed class EFCoreOutboxMonitor<TDbContext> : IOutboxMonitor
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;

    public EFCoreOutboxMonitor(TDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
        => _dbContext.Set<OutboxMessage>()
            .CountAsync(m => m.ProcessedAt == null && m.FailedAt == null, cancellationToken);

    /// <inheritdoc/>
    public Task<int> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
        => _dbContext.Set<OutboxMessage>()
            .CountAsync(m => m.FailedAt != null, cancellationToken);
}
