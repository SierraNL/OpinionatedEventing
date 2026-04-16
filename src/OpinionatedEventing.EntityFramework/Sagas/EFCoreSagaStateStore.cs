using Microsoft.EntityFrameworkCore;
using OpinionatedEventing.Sagas;

namespace OpinionatedEventing.EntityFramework.Sagas;

/// <summary>
/// EF Core implementation of <see cref="ISagaStateStore"/>.
/// Persists saga state in the same <typeparamref name="TDbContext"/> as the application.
/// </summary>
/// <typeparam name="TDbContext">The application's <see cref="DbContext"/> type.</typeparam>
internal sealed class EFCoreSagaStateStore<TDbContext> : ISagaStateStore
    where TDbContext : DbContext
{
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
    public async Task<IReadOnlyList<SagaState>> GetExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<SagaState>()
            .Where(s => s.ExpiresAt <= now && s.Status == SagaStatus.Active)
            .ToListAsync(cancellationToken);
    }
}
