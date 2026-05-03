using Microsoft.EntityFrameworkCore;
using OpinionatedEventing.EntityFramework.Tests.TestSupport;
using OpinionatedEventing.Outbox;
using Xunit;

namespace OpinionatedEventing.EntityFramework.Tests;

/// <summary>
/// Tests for <see cref="EFCoreOutboxStore{TDbContext}"/> covering all <see cref="IOutboxStore"/> operations
/// and the claim-column locking behaviour that prevents competing consumers from receiving duplicate messages.
/// Uses an in-process SQLite database because <c>ExecuteUpdateAsync</c> (used by the claim logic)
/// requires a relational provider.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EFCoreOutboxStoreTests : IDisposable
{
    // xUnit v3 creates a new class instance per test, so each test gets its own isolated database.
    private readonly SqliteDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private EFCoreOutboxStore<SqliteTestDbContext> CreateStore(SqliteTestDbContext context)
        => new(context, TimeProvider.System);

    private static OutboxMessage MakeMessage(string kind = "Event") => new()
    {
        Id = Guid.NewGuid(),
        MessageType = "SomeType, SomeAssembly",
        Payload = "{}",
        MessageKind = kind,
        CorrelationId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task SaveAsync_stages_message_without_saving()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        OutboxMessage message = MakeMessage();

        await store.SaveAsync(message, TestContext.Current.CancellationToken);

        Assert.Single(context.ChangeTracker.Entries<OutboxMessage>());
        Assert.Empty(await context.Set<OutboxMessage>().ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveAsync_persists_after_SaveChanges()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        OutboxMessage message = MakeMessage();

        await store.SaveAsync(message, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, await context.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPendingAsync_returns_unprocessed_messages_ordered_by_created_at()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        CancellationToken ct = TestContext.Current.CancellationToken;
        OutboxMessage older = new()
        {
            Id = Guid.NewGuid(), MessageType = "T, A", Payload = "{}",
            MessageKind = "Event", CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        };
        OutboxMessage newer = MakeMessage();

        await store.SaveAsync(older, ct);
        await store.SaveAsync(newer, ct);
        await context.SaveChangesAsync(ct);

        IReadOnlyList<OutboxMessage> pending = await store.GetPendingAsync(10, ct);

        Assert.Equal(2, pending.Count);
        Assert.Equal(older.Id, pending[0].Id);
        Assert.Equal(newer.Id, pending[1].Id);
    }

    [Fact]
    public async Task GetPendingAsync_excludes_processed_messages()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        CancellationToken ct = TestContext.Current.CancellationToken;
        OutboxMessage message = MakeMessage();

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);
        await store.MarkProcessedAsync(message.Id, ct);

        IReadOnlyList<OutboxMessage> pending = await store.GetPendingAsync(10, ct);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task GetPendingAsync_excludes_failed_messages()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        CancellationToken ct = TestContext.Current.CancellationToken;
        OutboxMessage message = MakeMessage();

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);
        await store.MarkFailedAsync(message.Id, "permanent error", ct);

        IReadOnlyList<OutboxMessage> pending = await store.GetPendingAsync(10, ct);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task GetPendingAsync_respects_batch_size()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        CancellationToken ct = TestContext.Current.CancellationToken;

        for (int i = 0; i < 5; i++)
            await store.SaveAsync(MakeMessage(), ct);
        await context.SaveChangesAsync(ct);

        IReadOnlyList<OutboxMessage> pending = await store.GetPendingAsync(3, ct);
        Assert.Equal(3, pending.Count);
    }

    [Fact]
    public async Task GetPendingAsync_excludes_locked_messages()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        CancellationToken ct = TestContext.Current.CancellationToken;
        OutboxMessage message = MakeMessage();

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);

        // First call claims the message.
        IReadOnlyList<OutboxMessage> first = await store.GetPendingAsync(10, ct);
        Assert.Single(first);

        // Second call on the same (or a fresh) context sees the message as locked and returns nothing.
        await using SqliteTestDbContext context2 = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store2 = CreateStore(context2);
        IReadOnlyList<OutboxMessage> second = await store2.GetPendingAsync(10, ct);
        Assert.Empty(second);
    }

    [Fact]
    public async Task GetPendingAsync_returns_message_after_lock_expires()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        CancellationToken ct = TestContext.Current.CancellationToken;
        OutboxMessage message = MakeMessage();

        // Manually insert a message with an already-expired lock.
        message.LockedBy = "stale-worker";
        message.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
        context.Set<OutboxMessage>().Add(message);
        await context.SaveChangesAsync(ct);

        await using SqliteTestDbContext context2 = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store2 = CreateStore(context2);

        IReadOnlyList<OutboxMessage> pending = await store2.GetPendingAsync(10, ct);

        Assert.Single(pending);
        Assert.Equal(message.Id, pending[0].Id);
    }

    [Fact]
    public async Task MarkProcessedAsync_sets_processed_at()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        CancellationToken ct = TestContext.Current.CancellationToken;
        OutboxMessage message = MakeMessage();

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);
        await store.MarkProcessedAsync(message.Id, ct);

        OutboxMessage? saved = await context.Set<OutboxMessage>().FindAsync([message.Id], ct);
        Assert.NotNull(saved!.ProcessedAt);
    }

    [Fact]
    public async Task MarkFailedAsync_sets_failed_at_and_error()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        CancellationToken ct = TestContext.Current.CancellationToken;
        OutboxMessage message = MakeMessage();

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);
        await store.MarkFailedAsync(message.Id, "broker unavailable", ct);

        OutboxMessage? saved = await context.Set<OutboxMessage>().FindAsync([message.Id], ct);
        Assert.NotNull(saved!.FailedAt);
        Assert.Equal("broker unavailable", saved.Error);
    }

    [Fact]
    public async Task IncrementAttemptAsync_increments_count_and_sets_error()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        CancellationToken ct = TestContext.Current.CancellationToken;
        OutboxMessage message = MakeMessage();

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);
        await store.IncrementAttemptAsync(message.Id, "transient", null, ct);

        OutboxMessage? saved = await context.Set<OutboxMessage>().FindAsync([message.Id], ct);
        Assert.Equal(1, saved!.AttemptCount);
        Assert.Equal("transient", saved.Error);
    }

    [Fact]
    public async Task IncrementAttemptAsync_message_remains_pending()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        CancellationToken ct = TestContext.Current.CancellationToken;
        OutboxMessage message = MakeMessage();

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);
        await store.IncrementAttemptAsync(message.Id, "transient", null, ct);

        IReadOnlyList<OutboxMessage> pending = await store.GetPendingAsync(10, ct);
        Assert.Single(pending);
    }

    [Fact]
    public async Task IncrementAttemptAsync_sets_next_attempt_at()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        CancellationToken ct = TestContext.Current.CancellationToken;
        OutboxMessage message = MakeMessage();
        DateTimeOffset nextAttempt = DateTimeOffset.UtcNow.AddSeconds(30);

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);
        await store.IncrementAttemptAsync(message.Id, "transient", nextAttempt, ct);

        OutboxMessage? saved = await context.Set<OutboxMessage>().FindAsync([message.Id], ct);
        Assert.NotNull(saved!.NextAttemptAt);
    }

    [Fact]
    public async Task GetPendingAsync_excludes_messages_before_next_attempt_at()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        CancellationToken ct = TestContext.Current.CancellationToken;
        OutboxMessage message = MakeMessage();

        message.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(5);
        context.Set<OutboxMessage>().Add(message);
        await context.SaveChangesAsync(ct);

        await using SqliteTestDbContext context2 = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store2 = CreateStore(context2);
        IReadOnlyList<OutboxMessage> pending = await store2.GetPendingAsync(10, ct);

        Assert.Empty(pending);
    }

    [Fact]
    public async Task GetPendingAsync_returns_message_when_next_attempt_at_elapsed()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        CancellationToken ct = TestContext.Current.CancellationToken;
        OutboxMessage message = MakeMessage();

        message.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        context.Set<OutboxMessage>().Add(message);
        await context.SaveChangesAsync(ct);

        await using SqliteTestDbContext context2 = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store2 = CreateStore(context2);
        IReadOnlyList<OutboxMessage> pending = await store2.GetPendingAsync(10, ct);

        Assert.Single(pending);
        Assert.Equal(message.Id, pending[0].Id);
    }

    [Fact]
    public async Task DeleteProcessedAsync_removes_rows_older_than_cutoff()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);

        OutboxMessage old = MakeMessage();
        OutboxMessage recent = MakeMessage();

        await store.SaveAsync(old, ct);
        await store.SaveAsync(recent, ct);
        await context.SaveChangesAsync(ct);

        DateTimeOffset cutoff = DateTimeOffset.UtcNow;
        old.ProcessedAt = cutoff.AddDays(-8);
        recent.ProcessedAt = cutoff.AddDays(-1);
        await context.SaveChangesAsync(ct);

        int deleted = await store.DeleteProcessedAsync(cutoff.AddDays(-7), ct);

        Assert.Equal(1, deleted);

        // ExecuteDeleteAsync bypasses the EF change tracker, so use a fresh context to verify.
        await using SqliteTestDbContext verifyContext = _factory.CreateContext();
        Assert.Null(await verifyContext.Set<OutboxMessage>().FindAsync([old.Id], ct));
        Assert.NotNull(await verifyContext.Set<OutboxMessage>().FindAsync([recent.Id], ct));
    }

    [Fact]
    public async Task DeleteProcessedAsync_returns_zero_when_nothing_matches()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        CancellationToken ct = TestContext.Current.CancellationToken;

        int deleted = await store.DeleteProcessedAsync(DateTimeOffset.UtcNow.AddDays(-7), ct);

        Assert.Equal(0, deleted);
    }

    [Fact]
    public async Task DeleteFailedAsync_removes_rows_older_than_cutoff()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);

        OutboxMessage old = MakeMessage();
        OutboxMessage recent = MakeMessage();

        await store.SaveAsync(old, ct);
        await store.SaveAsync(recent, ct);
        await context.SaveChangesAsync(ct);

        DateTimeOffset cutoff = DateTimeOffset.UtcNow;
        old.FailedAt = cutoff.AddDays(-8);
        recent.FailedAt = cutoff.AddDays(-1);
        await context.SaveChangesAsync(ct);

        int deleted = await store.DeleteFailedAsync(cutoff.AddDays(-7), ct);

        Assert.Equal(1, deleted);

        // ExecuteDeleteAsync bypasses the EF change tracker, so use a fresh context to verify.
        await using SqliteTestDbContext verifyContext = _factory.CreateContext();
        Assert.Null(await verifyContext.Set<OutboxMessage>().FindAsync([old.Id], ct));
        Assert.NotNull(await verifyContext.Set<OutboxMessage>().FindAsync([recent.Id], ct));
    }

    [Fact]
    public async Task DeleteFailedAsync_does_not_delete_pending_rows()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store = CreateStore(context);
        CancellationToken ct = TestContext.Current.CancellationToken;
        OutboxMessage message = MakeMessage();

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);

        int deleted = await store.DeleteFailedAsync(DateTimeOffset.UtcNow.AddDays(1), ct);

        Assert.Equal(0, deleted);
        Assert.NotNull(await context.Set<OutboxMessage>().FindAsync([message.Id], ct));
    }
}
