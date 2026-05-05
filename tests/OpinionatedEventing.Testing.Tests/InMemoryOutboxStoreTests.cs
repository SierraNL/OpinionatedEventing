using OpinionatedEventing.Outbox;
using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Tests;

public sealed class InMemoryOutboxStoreTests
{
    private static OutboxMessage MakeMessage() => new()
    {
        Id = Guid.NewGuid(),
        MessageType = "TestMessage",
        MessageKind = MessageKind.Event,
        Payload = "{}",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task SaveAsync_MessageAppearsInPendingMessages()
    {
        var store = new InMemoryOutboxStore();
        var msg = MakeMessage();
        await store.SaveAsync(msg, TestContext.Current.CancellationToken);

        Assert.Single(store.PendingMessages);
        Assert.Empty(store.ProcessedMessages);
    }

    [Fact]
    public async Task MarkProcessedAsync_MovesMessageToProcessedMessages()
    {
        var store = new InMemoryOutboxStore();
        var msg = MakeMessage();
        await store.SaveAsync(msg, TestContext.Current.CancellationToken);
        await store.MarkProcessedAsync(msg.Id, TestContext.Current.CancellationToken);

        Assert.Empty(store.PendingMessages);
        Assert.Single(store.ProcessedMessages);
    }

    [Fact]
    public async Task MarkFailedAsync_RemovesMessageFromPendingMessages()
    {
        var store = new InMemoryOutboxStore();
        var msg = MakeMessage();
        await store.SaveAsync(msg, TestContext.Current.CancellationToken);
        await store.MarkFailedAsync(msg.Id, "boom", TestContext.Current.CancellationToken);

        Assert.Empty(store.PendingMessages);
        Assert.Empty(store.ProcessedMessages);
    }

    [Fact]
    public async Task Messages_ContainsAllMessagesRegardlessOfStatus()
    {
        var store = new InMemoryOutboxStore();
        var pending = MakeMessage();
        var processed = MakeMessage();
        var failed = MakeMessage();

        await store.SaveAsync(pending, TestContext.Current.CancellationToken);
        await store.SaveAsync(processed, TestContext.Current.CancellationToken);
        await store.SaveAsync(failed, TestContext.Current.CancellationToken);
        await store.MarkProcessedAsync(processed.Id, TestContext.Current.CancellationToken);
        await store.MarkFailedAsync(failed.Id, "err", TestContext.Current.CancellationToken);

        Assert.Equal(3, store.Messages.Count);
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsPendingMessagesUpToBatchSize()
    {
        var store = new InMemoryOutboxStore();
        for (var i = 0; i < 5; i++)
            await store.SaveAsync(MakeMessage(), TestContext.Current.CancellationToken);

        var batch = await store.GetPendingAsync(3, TestContext.Current.CancellationToken);
        Assert.Equal(3, batch.Count);
    }

    [Fact]
    public async Task IncrementAttemptAsync_IncrementsCountSetsErrorAndNextAttemptAt()
    {
        var store = new InMemoryOutboxStore();
        var msg = MakeMessage();
        await store.SaveAsync(msg, TestContext.Current.CancellationToken);
        DateTimeOffset next = DateTimeOffset.UtcNow.AddSeconds(30);

        await store.IncrementAttemptAsync(msg.Id, "transient", next, TestContext.Current.CancellationToken);

        var updated = Assert.Single(store.Messages);
        Assert.Equal(1, updated.AttemptCount);
        Assert.Equal("transient", updated.Error);
        Assert.Equal(next, updated.NextAttemptAt);
    }

    [Fact]
    public async Task GetPendingAsync_ExcludesMessageWithFutureNextAttemptAt()
    {
        var store = new InMemoryOutboxStore();
        var msg = MakeMessage();
        msg.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await store.SaveAsync(msg, TestContext.Current.CancellationToken);

        var batch = await store.GetPendingAsync(10, TestContext.Current.CancellationToken);
        Assert.Empty(batch);
    }

    [Fact]
    public async Task GetPendingAsync_IncludesMessageWithElapsedNextAttemptAt()
    {
        var store = new InMemoryOutboxStore();
        var msg = MakeMessage();
        msg.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await store.SaveAsync(msg, TestContext.Current.CancellationToken);

        var batch = await store.GetPendingAsync(10, TestContext.Current.CancellationToken);
        Assert.Single(batch);
    }

    [Fact]
    public async Task DeleteProcessedAsync_RemovesRowsOlderThanCutoff()
    {
        var store = new InMemoryOutboxStore();
        var ct = TestContext.Current.CancellationToken;

        var old = MakeMessage();
        var recent = MakeMessage();
        await store.SaveAsync(old, ct);
        await store.SaveAsync(recent, ct);
        old.ProcessedAt = DateTimeOffset.UtcNow.AddDays(-8);
        recent.ProcessedAt = DateTimeOffset.UtcNow.AddDays(-1);

        int deleted = await store.DeleteProcessedAsync(DateTimeOffset.UtcNow.AddDays(-7), ct);

        Assert.Equal(1, deleted);
        Assert.DoesNotContain(store.Messages, m => m.Id == old.Id);
        Assert.Contains(store.Messages, m => m.Id == recent.Id);
    }

    [Fact]
    public async Task DeleteFailedAsync_RemovesRowsOlderThanCutoff()
    {
        var store = new InMemoryOutboxStore();
        var ct = TestContext.Current.CancellationToken;

        var old = MakeMessage();
        var recent = MakeMessage();
        await store.SaveAsync(old, ct);
        await store.SaveAsync(recent, ct);
        old.FailedAt = DateTimeOffset.UtcNow.AddDays(-8);
        recent.FailedAt = DateTimeOffset.UtcNow.AddDays(-1);

        int deleted = await store.DeleteFailedAsync(DateTimeOffset.UtcNow.AddDays(-7), ct);

        Assert.Equal(1, deleted);
        Assert.DoesNotContain(store.Messages, m => m.Id == old.Id);
        Assert.Contains(store.Messages, m => m.Id == recent.Id);
    }

    [Fact]
    public async Task DeleteProcessedAsync_DoesNotDeletePendingRows()
    {
        var store = new InMemoryOutboxStore();
        var ct = TestContext.Current.CancellationToken;
        var msg = MakeMessage();
        await store.SaveAsync(msg, ct);

        int deleted = await store.DeleteProcessedAsync(DateTimeOffset.UtcNow.AddDays(1), ct);

        Assert.Equal(0, deleted);
        Assert.Single(store.Messages);
    }
}
