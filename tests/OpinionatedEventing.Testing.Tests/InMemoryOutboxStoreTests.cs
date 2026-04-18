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
        MessageKind = "Event",
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
}
