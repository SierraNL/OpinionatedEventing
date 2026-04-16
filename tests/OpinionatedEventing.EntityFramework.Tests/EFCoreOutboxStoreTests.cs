using OpinionatedEventing.EntityFramework.Tests.TestSupport;
using OpinionatedEventing.Outbox;
using Xunit;

namespace OpinionatedEventing.EntityFramework.Tests;

public sealed class EFCoreOutboxStoreTests : IDisposable
{
    private readonly InMemoryDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private EFCoreOutboxStore<TestDbContext> CreateStore(TestDbContext context)
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
        await using var context = _factory.CreateContext();
        var store = CreateStore(context);
        var message = MakeMessage();

        await store.SaveAsync(message, TestContext.Current.CancellationToken);

        Assert.Single(context.ChangeTracker.Entries<OutboxMessage>());
        Assert.Empty(context.Set<OutboxMessage>());
    }

    [Fact]
    public async Task SaveAsync_persists_after_SaveChanges()
    {
        await using var context = _factory.CreateContext();
        var store = CreateStore(context);
        var message = MakeMessage();

        await store.SaveAsync(message, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, context.Set<OutboxMessage>().Count());
    }

    [Fact]
    public async Task GetPendingAsync_returns_unprocessed_messages_ordered_by_created_at()
    {
        await using var context = _factory.CreateContext();
        var store = CreateStore(context);
        var ct = TestContext.Current.CancellationToken;
        var older = new OutboxMessage
        {
            Id = Guid.NewGuid(), MessageType = "T, A", Payload = "{}",
            MessageKind = "Event", CorrelationId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        };
        var newer = MakeMessage();

        await store.SaveAsync(older, ct);
        await store.SaveAsync(newer, ct);
        await context.SaveChangesAsync(ct);

        var pending = await store.GetPendingAsync(10, ct);

        Assert.Equal(2, pending.Count);
        Assert.Equal(older.Id, pending[0].Id);
        Assert.Equal(newer.Id, pending[1].Id);
    }

    [Fact]
    public async Task GetPendingAsync_excludes_processed_messages()
    {
        await using var context = _factory.CreateContext();
        var store = CreateStore(context);
        var ct = TestContext.Current.CancellationToken;
        var message = MakeMessage();

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);
        await store.MarkProcessedAsync(message.Id, ct);

        var pending = await store.GetPendingAsync(10, ct);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task GetPendingAsync_excludes_failed_messages()
    {
        await using var context = _factory.CreateContext();
        var store = CreateStore(context);
        var ct = TestContext.Current.CancellationToken;
        var message = MakeMessage();

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);
        await store.MarkFailedAsync(message.Id, "permanent error", ct);

        var pending = await store.GetPendingAsync(10, ct);
        Assert.Empty(pending);
    }

    [Fact]
    public async Task GetPendingAsync_respects_batch_size()
    {
        await using var context = _factory.CreateContext();
        var store = CreateStore(context);
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 5; i++)
            await store.SaveAsync(MakeMessage(), ct);
        await context.SaveChangesAsync(ct);

        var pending = await store.GetPendingAsync(3, ct);
        Assert.Equal(3, pending.Count);
    }

    [Fact]
    public async Task MarkProcessedAsync_sets_processed_at()
    {
        await using var context = _factory.CreateContext();
        var store = CreateStore(context);
        var ct = TestContext.Current.CancellationToken;
        var message = MakeMessage();

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);
        await store.MarkProcessedAsync(message.Id, ct);

        var saved = await context.Set<OutboxMessage>().FindAsync([message.Id], ct);
        Assert.NotNull(saved!.ProcessedAt);
    }

    [Fact]
    public async Task MarkFailedAsync_sets_failed_at_and_error()
    {
        await using var context = _factory.CreateContext();
        var store = CreateStore(context);
        var ct = TestContext.Current.CancellationToken;
        var message = MakeMessage();

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);
        await store.MarkFailedAsync(message.Id, "broker unavailable", ct);

        var saved = await context.Set<OutboxMessage>().FindAsync([message.Id], ct);
        Assert.NotNull(saved!.FailedAt);
        Assert.Equal("broker unavailable", saved.Error);
    }

    [Fact]
    public async Task IncrementAttemptAsync_increments_count_and_sets_error()
    {
        await using var context = _factory.CreateContext();
        var store = CreateStore(context);
        var ct = TestContext.Current.CancellationToken;
        var message = MakeMessage();

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);
        await store.IncrementAttemptAsync(message.Id, "transient", ct);

        var saved = await context.Set<OutboxMessage>().FindAsync([message.Id], ct);
        Assert.Equal(1, saved!.AttemptCount);
        Assert.Equal("transient", saved.Error);
    }

    [Fact]
    public async Task IncrementAttemptAsync_message_remains_pending()
    {
        await using var context = _factory.CreateContext();
        var store = CreateStore(context);
        var ct = TestContext.Current.CancellationToken;
        var message = MakeMessage();

        await store.SaveAsync(message, ct);
        await context.SaveChangesAsync(ct);
        await store.IncrementAttemptAsync(message.Id, "transient", ct);

        var pending = await store.GetPendingAsync(10, ct);
        Assert.Single(pending);
    }
}
