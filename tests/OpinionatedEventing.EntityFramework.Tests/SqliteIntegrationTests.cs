using OpinionatedEventing.EntityFramework.Sagas;
using OpinionatedEventing.EntityFramework.Tests.TestSupport;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Sagas;
using Xunit;

namespace OpinionatedEventing.EntityFramework.Tests;

/// <summary>
/// Integration tests verifying that pending-message dispatch and saga timeout queries
/// work correctly against an in-process SQLite database using UTC-ticks storage for
/// <see cref="DateTimeOffset"/> columns.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SqliteIntegrationTests : IDisposable
{
    // xUnit v3 creates a new class instance per test method, so each test gets its own
    // factory (and therefore its own isolated in-memory SQLite database).
    private readonly SqliteDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private EFCoreOutboxStore<SqliteTestDbContext> CreateOutboxStore(SqliteTestDbContext ctx)
        => new(ctx, TimeProvider.System);

    private EFCoreSagaStateStore<SqliteTestDbContext> CreateSagaStore(SqliteTestDbContext ctx)
        => new(ctx);

    private static OutboxMessage MakeMessage(DateTimeOffset? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        MessageType = "SomeType, SomeAssembly",
        Payload = "{}",
        MessageKind = "Event",
        CorrelationId = Guid.NewGuid(),
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
    };

    private static SagaState MakeSagaState(DateTimeOffset? expiresAt = null) => new()
    {
        Id = Guid.NewGuid(),
        SagaType = "OrderSaga",
        CorrelationId = Guid.NewGuid().ToString(),
        State = "{}",
        Status = SagaStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = expiresAt,
    };

    // --- Outbox ---

    [Fact]
    public async Task GetPendingAsync_returns_messages_ordered_by_CreatedAt_on_SQLite()
    {
        await using var ctx = _factory.CreateContext();
        var store = CreateOutboxStore(ctx);
        var ct = TestContext.Current.CancellationToken;

        var older = MakeMessage(DateTimeOffset.UtcNow.AddMinutes(-5));
        var newer = MakeMessage(DateTimeOffset.UtcNow);

        await store.SaveAsync(older, ct);
        await store.SaveAsync(newer, ct);
        await ctx.SaveChangesAsync(ct);

        var pending = await store.GetPendingAsync(10, ct);

        Assert.Equal(2, pending.Count);
        Assert.Equal(older.Id, pending[0].Id);
        Assert.Equal(newer.Id, pending[1].Id);
    }

    [Fact]
    public async Task GetPendingAsync_excludes_processed_and_failed_messages_on_SQLite()
    {
        await using var ctx = _factory.CreateContext();
        var store = CreateOutboxStore(ctx);
        var ct = TestContext.Current.CancellationToken;

        var pending = MakeMessage();
        var processed = MakeMessage(DateTimeOffset.UtcNow.AddMinutes(-2));
        var failed = MakeMessage(DateTimeOffset.UtcNow.AddMinutes(-1));

        await store.SaveAsync(pending, ct);
        await store.SaveAsync(processed, ct);
        await store.SaveAsync(failed, ct);
        await ctx.SaveChangesAsync(ct);

        await store.MarkProcessedAsync(processed.Id, ct);
        await store.MarkFailedAsync(failed.Id, "error", ct);

        var results = await store.GetPendingAsync(10, ct);

        Assert.Single(results);
        Assert.Equal(pending.Id, results[0].Id);
    }

    [Fact]
    public async Task MarkProcessedAsync_sets_ProcessedAt_and_message_leaves_pending_queue_on_SQLite()
    {
        await using var ctx = _factory.CreateContext();
        var store = CreateOutboxStore(ctx);
        var ct = TestContext.Current.CancellationToken;
        var message = MakeMessage();

        await store.SaveAsync(message, ct);
        await ctx.SaveChangesAsync(ct);
        await store.MarkProcessedAsync(message.Id, ct);

        var saved = await ctx.Set<OutboxMessage>().FindAsync([message.Id], ct);
        Assert.NotNull(saved!.ProcessedAt);
        Assert.Empty(await store.GetPendingAsync(10, ct));
    }

    [Fact]
    public async Task GetPendingAsync_competing_consumers_receive_disjoint_batches()
    {
        // Verifies the claim-column invariant: once worker A has claimed a batch, worker B
        // receives only the unclaimed remainder — no message appears in both batches.
        // The test is sequential because the SQLite factory shares a single connection, but
        // the claim-column approach provides the same safety under true concurrency: the
        // re-check UPDATE is atomic at the row level regardless of which worker runs first.
        var ct = TestContext.Current.CancellationToken;

        // Arrange: seed 6 messages so two workers each fetching up to 4 must share the pool.
        await using (var seedCtx = _factory.CreateContext())
        {
            for (int i = 0; i < 6; i++)
                seedCtx.Set<OutboxMessage>().Add(MakeMessage(DateTimeOffset.UtcNow.AddSeconds(-i)));
            await seedCtx.SaveChangesAsync(ct);
        }

        // Act: worker A claims first.
        await using var ctx1 = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store1 = CreateOutboxStore(ctx1);
        IReadOnlyList<OutboxMessage> batch1 = await store1.GetPendingAsync(4, ct);

        // Worker B claims after — should get at most 4 of the unclaimed remainder.
        await using var ctx2 = _factory.CreateContext();
        EFCoreOutboxStore<SqliteTestDbContext> store2 = CreateOutboxStore(ctx2);
        IReadOnlyList<OutboxMessage> batch2 = await store2.GetPendingAsync(4, ct);

        // Assert: no message appears in both batches.
        HashSet<Guid> ids1 = batch1.Select(m => m.Id).ToHashSet();
        HashSet<Guid> ids2 = batch2.Select(m => m.Id).ToHashSet();
        Assert.Empty(ids1.Intersect(ids2));

        // Together they cover all 6 messages without any duplicates.
        Assert.Equal(6, ids1.Union(ids2).Count());
    }

    // --- Saga state ---

    [Fact]
    public async Task GetExpiredAsync_returns_states_whose_ExpiresAt_is_in_the_past_on_SQLite()
    {
        await using var ctx = _factory.CreateContext();
        var store = CreateSagaStore(ctx);
        var ct = TestContext.Current.CancellationToken;

        var expired = MakeSagaState(DateTimeOffset.UtcNow.AddMinutes(-1));
        var notExpired = MakeSagaState(DateTimeOffset.UtcNow.AddHours(1));
        var noTimeout = MakeSagaState(expiresAt: null);

        await store.SaveAsync(expired, ct);
        await store.SaveAsync(notExpired, ct);
        await store.SaveAsync(noTimeout, ct);

        var results = await store.GetExpiredAsync(DateTimeOffset.UtcNow, ct);

        Assert.Single(results);
        Assert.Equal(expired.Id, results[0].Id);
    }

    [Fact]
    public async Task GetExpiredAsync_competing_workers_receive_disjoint_saga_sets_on_SQLite()
    {
        // Verifies the claim-column invariant at the SQLite layer: once worker A has claimed an
        // expired saga, worker B receives only the unclaimed remainder — no saga appears in both.
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        // Seed 6 expired sagas.
        await using (var seedCtx = _factory.CreateContext())
        {
            var store = CreateSagaStore(seedCtx);
            for (int i = 0; i < 6; i++)
                await store.SaveAsync(MakeSagaState(now.AddHours(-1)), ct);
        }

        // Worker A claims first.
        await using var ctx1 = _factory.CreateContext();
        var store1 = CreateSagaStore(ctx1);
        IReadOnlyList<SagaState> batch1 = await store1.GetExpiredAsync(now, ct);

        // Worker B claims after — should only get the unclaimed remainder.
        await using var ctx2 = _factory.CreateContext();
        var store2 = CreateSagaStore(ctx2);
        IReadOnlyList<SagaState> batch2 = await store2.GetExpiredAsync(now, ct);

        // No saga appears in both batches.
        HashSet<Guid> ids1 = batch1.Select(s => s.Id).ToHashSet();
        HashSet<Guid> ids2 = batch2.Select(s => s.Id).ToHashSet();
        Assert.Empty(ids1.Intersect(ids2));

        // Together they cover all 6 sagas without any duplicates.
        Assert.Equal(6, ids1.Union(ids2).Count());
    }

    [Fact]
    public async Task FindAsync_round_trips_SagaState_through_SQLite()
    {
        await using var ctx = _factory.CreateContext();
        var store = CreateSagaStore(ctx);
        var ct = TestContext.Current.CancellationToken;

        var state = MakeSagaState(DateTimeOffset.UtcNow.AddHours(1));
        await store.SaveAsync(state, ct);

        var found = await store.FindAsync(state.SagaType, state.CorrelationId, ct);

        Assert.NotNull(found);
        Assert.Equal(state.Id, found.Id);
        Assert.Equal(SagaStatus.Active, found.Status);
        Assert.NotNull(found.ExpiresAt);
    }
}
