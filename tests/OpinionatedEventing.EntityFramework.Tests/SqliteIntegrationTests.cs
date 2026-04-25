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
