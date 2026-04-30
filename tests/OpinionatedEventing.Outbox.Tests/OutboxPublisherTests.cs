#nullable enable

using System.Text.Json;
using Microsoft.Extensions.Options;
using OpinionatedEventing;
using OpinionatedEventing.Options;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Outbox.Tests;

public sealed class OutboxPublisherTests
{
    // ---- helpers ----

    private static (IPublisher Publisher, InMemoryOutboxStore Store) BuildPublisher(
        Action<OpinionatedEventingOptions>? configure = null,
        IOutboxTransactionGuard? guard = null,
        Guid? correlationId = null,
        Guid? causationId = null)
    {
        var store = new InMemoryOutboxStore();
        var context = new FakeMessagingContext(correlationId ?? Guid.NewGuid(), causationId);
        var optionsValue = new OpinionatedEventingOptions();
        configure?.Invoke(optionsValue);
        var options = Microsoft.Extensions.Options.Options.Create(optionsValue);
        var registry = new MessageTypeRegistry();

        IEnumerable<IOutboxTransactionGuard> guards = guard is not null ? [guard] : [];
        var publisher = new OutboxPublisher(store, context, registry, options, TimeProvider.System, guards);
        return (publisher, store);
    }

    // ---- PublishEventAsync ----

    [Fact]
    public async Task PublishEventAsync_SavesMessageWithEventKind()
    {
        var (publisher, store) = BuildPublisher();

        await publisher.PublishEventAsync(new TestEvent(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Single(store.Messages);
        Assert.Equal("Event", store.Messages[0].MessageKind);
    }

    [Fact]
    public async Task PublishEventAsync_SetsMessageTypeToStableIdentifier()
    {
        var (publisher, store) = BuildPublisher();

        await publisher.PublishEventAsync(new TestEvent(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(typeof(TestEvent).FullName, store.Messages[0].MessageType);
    }

    [Fact]
    public async Task PublishEventAsync_SerializesPayload()
    {
        var (publisher, store) = BuildPublisher();
        var id = Guid.NewGuid();

        await publisher.PublishEventAsync(new TestEvent(id), TestContext.Current.CancellationToken);

        var deserialized = JsonSerializer.Deserialize<TestEvent>(store.Messages[0].Payload);
        Assert.Equal(id, deserialized!.Id);
    }

    [Fact]
    public async Task PublishEventAsync_StampsCorrelationAndCausationIds()
    {
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var (publisher, store) = BuildPublisher(correlationId: correlationId, causationId: causationId);

        await publisher.PublishEventAsync(new TestEvent(Guid.NewGuid()), TestContext.Current.CancellationToken);

        var msg = store.Messages[0];
        Assert.Equal(correlationId, msg.CorrelationId);
        Assert.Equal(causationId, msg.CausationId);
    }

    [Fact]
    public async Task PublishEventAsync_SetsCreatedAt()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var (publisher, store) = BuildPublisher();

        await publisher.PublishEventAsync(new TestEvent(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.InRange(store.Messages[0].CreatedAt, before, DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task PublishEventAsync_AssignsUniqueIds()
    {
        var (publisher, store) = BuildPublisher();

        await publisher.PublishEventAsync(new TestEvent(Guid.NewGuid()), TestContext.Current.CancellationToken);
        await publisher.PublishEventAsync(new TestEvent(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(2, store.Messages.Count);
        Assert.NotEqual(store.Messages[0].Id, store.Messages[1].Id);
    }

    // ---- SendCommandAsync ----

    [Fact]
    public async Task SendCommandAsync_SavesMessageWithCommandKind()
    {
        var (publisher, store) = BuildPublisher();

        await publisher.SendCommandAsync(new TestCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Single(store.Messages);
        Assert.Equal("Command", store.Messages[0].MessageKind);
    }

    [Fact]
    public async Task SendCommandAsync_SetsMessageTypeToStableIdentifier()
    {
        var (publisher, store) = BuildPublisher();

        await publisher.SendCommandAsync(new TestCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(typeof(TestCommand).FullName, store.Messages[0].MessageType);
    }

    // ---- Transaction guard ----

    [Fact]
    public async Task PublishEventAsync_CallsTransactionGuard()
    {
        var guard = new FakeTransactionGuard();
        var (publisher, _) = BuildPublisher(guard: guard);

        await publisher.PublishEventAsync(new TestEvent(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(1, guard.EnsureTransactionCallCount);
    }

    [Fact]
    public async Task PublishEventAsync_PropagatesTransactionGuardException()
    {
        var guard = new ThrowingTransactionGuard();
        var (publisher, _) = BuildPublisher(guard: guard);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishEventAsync(new TestEvent(Guid.NewGuid()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendCommandAsync_CallsTransactionGuard()
    {
        var guard = new FakeTransactionGuard();
        var (publisher, _) = BuildPublisher(guard: guard);

        await publisher.SendCommandAsync(new TestCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(1, guard.EnsureTransactionCallCount);
    }

    // ---- Custom serializer options ----

    [Fact]
    public async Task PublishEventAsync_UsesConfiguredSerializerOptions()
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var (publisher, store) = BuildPublisher(configure: o => o.SerializerOptions = jsonOptions);

        await publisher.PublishEventAsync(new TestEvent(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Contains("\"id\"", store.Messages[0].Payload);
    }

    // ---- fakes ----

    private sealed record TestEvent(Guid Id) : IEvent;
    private sealed record TestCommand(Guid Id) : ICommand;

    private sealed class FakeMessagingContext(Guid correlationId, Guid? causationId) : IMessagingContext
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public Guid CorrelationId { get; } = correlationId;
        public Guid? CausationId { get; } = causationId;
    }

    private sealed class FakeTransactionGuard : IOutboxTransactionGuard
    {
        public int EnsureTransactionCallCount { get; private set; }
        public void EnsureTransaction() => EnsureTransactionCallCount++;
    }

    private sealed class ThrowingTransactionGuard : IOutboxTransactionGuard
    {
        public void EnsureTransaction() =>
            throw new InvalidOperationException("No active transaction.");
    }
}
