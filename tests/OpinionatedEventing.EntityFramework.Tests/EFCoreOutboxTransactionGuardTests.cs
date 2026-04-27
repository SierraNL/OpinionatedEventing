using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpinionatedEventing.EntityFramework.Tests.TestSupport;
using OpinionatedEventing.Outbox;
using System.Transactions;
using Xunit;

namespace OpinionatedEventing.EntityFramework.Tests;

/// <summary>
/// Tests for <see cref="EFCoreOutboxTransactionGuard{TDbContext}"/>.
/// Uses SQLite because <c>Database.CurrentTransaction</c> requires a relational provider.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EFCoreOutboxTransactionGuardTests : IDisposable
{
    private readonly SqliteDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private EFCoreOutboxTransactionGuard<SqliteTestDbContext> CreateGuard(SqliteTestDbContext context)
        => new(context);

    [Fact]
    public void EnsureTransaction_throws_when_no_transaction_is_active()
    {
        using SqliteTestDbContext context = _factory.CreateContext();
        var guard = CreateGuard(context);

        Assert.Throws<InvalidOperationException>(() => guard.EnsureTransaction());
    }

    [Fact]
    public async Task EnsureTransaction_does_not_throw_inside_BeginTransactionAsync()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        var guard = CreateGuard(context);

        await using IDbContextTransaction tx = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        guard.EnsureTransaction(); // must not throw
    }

    [Fact]
    public void EnsureTransaction_does_not_throw_inside_TransactionScope()
    {
        using SqliteTestDbContext context = _factory.CreateContext();
        var guard = CreateGuard(context);

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        guard.EnsureTransaction(); // must not throw
    }
}
