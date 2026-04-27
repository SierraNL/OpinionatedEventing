using Microsoft.EntityFrameworkCore;
using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.EntityFramework;

/// <summary>
/// EF Core implementation of <see cref="IOutboxTransactionGuard"/>.
/// Verifies that either an explicit EF Core database transaction or an ambient
/// <see cref="System.Transactions.TransactionScope"/> is active before a message is written to the outbox.
/// </summary>
/// <typeparam name="TDbContext">The application's <see cref="DbContext"/> type.</typeparam>
internal sealed class EFCoreOutboxTransactionGuard<TDbContext> : IOutboxTransactionGuard
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;

    public EFCoreOutboxTransactionGuard(TDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Thrown when neither an EF Core database transaction nor an ambient
    /// <see cref="System.Transactions.TransactionScope"/> is active.
    /// </exception>
    public void EnsureTransaction()
    {
        if (_dbContext.Database.CurrentTransaction is not null ||
            System.Transactions.Transaction.Current is not null)
        {
            return;
        }

        throw new InvalidOperationException(
            "IPublisher was called outside an active transaction. " +
            "Wrap the call inside a database transaction (e.g. await db.Database.BeginTransactionAsync()) " +
            "or a TransactionScope so the outbox message is committed atomically with your business data.");
    }
}
