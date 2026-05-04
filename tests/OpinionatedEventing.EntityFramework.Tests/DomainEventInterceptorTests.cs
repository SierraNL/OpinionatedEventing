using Microsoft.Extensions.Options;
using OpinionatedEventing;
using OpinionatedEventing.EntityFramework.Tests.TestSupport;
using OpinionatedEventing.Options;
using OpinionatedEventing.Outbox;
using Xunit;

namespace OpinionatedEventing.EntityFramework.Tests;

public sealed class DomainEventInterceptorTests : IDisposable
{
    private readonly InMemoryDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static DomainEventInterceptor CreateInterceptor(
        Guid? correlationId = null,
        Guid? causationId = null)
    {
        var context = new FakeMessagingContext(correlationId ?? Guid.NewGuid(), causationId);
        var options = Microsoft.Extensions.Options.Options.Create(new OpinionatedEventingOptions());
        var registry = new MessageTypeRegistry();
        return new DomainEventInterceptor(context, registry, options, TimeProvider.System);
    }

    private TestDbContext CreateContextWithInterceptor(DomainEventInterceptor interceptor)
        => new(_factory.CreateOptionsBuilder().AddInterceptors(interceptor).Options);

    [Fact]
    public async Task SaveChanges_writes_domain_events_to_outbox()
    {
        var interceptor = CreateInterceptor();
        await using var context = CreateContextWithInterceptor(interceptor);
        var ct = TestContext.Current.CancellationToken;

        var order = TestOrder.Place(Guid.NewGuid());
        context.Set<TestOrder>().Add(order);
        await context.SaveChangesAsync(ct);

        var outboxMessages = context.Set<OutboxMessage>().ToList();
        Assert.Single(outboxMessages);
        Assert.Equal(MessageKind.Event, outboxMessages[0].MessageKind);
    }

    [Fact]
    public async Task SaveChanges_clears_domain_events_after_harvest()
    {
        var interceptor = CreateInterceptor();
        await using var context = CreateContextWithInterceptor(interceptor);
        var ct = TestContext.Current.CancellationToken;

        var order = TestOrder.Place(Guid.NewGuid());
        context.Set<TestOrder>().Add(order);
        await context.SaveChangesAsync(ct);

        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public async Task SaveChanges_stamps_correlation_id_from_messaging_context()
    {
        var correlationId = Guid.NewGuid();
        var interceptor = CreateInterceptor(correlationId: correlationId);
        await using var context = CreateContextWithInterceptor(interceptor);
        var ct = TestContext.Current.CancellationToken;

        var order = TestOrder.Place(Guid.NewGuid());
        context.Set<TestOrder>().Add(order);
        await context.SaveChangesAsync(ct);

        var message = context.Set<OutboxMessage>().Single();
        Assert.Equal(correlationId, message.CorrelationId);
    }

    [Fact]
    public async Task SaveChanges_stamps_causation_id_from_messaging_context()
    {
        var causationId = Guid.NewGuid();
        var interceptor = CreateInterceptor(causationId: causationId);
        await using var context = CreateContextWithInterceptor(interceptor);
        var ct = TestContext.Current.CancellationToken;

        var order = TestOrder.Place(Guid.NewGuid());
        context.Set<TestOrder>().Add(order);
        await context.SaveChangesAsync(ct);

        var message = context.Set<OutboxMessage>().Single();
        Assert.Equal(causationId, message.CausationId);
    }

    [Fact]
    public async Task SaveChanges_causation_id_is_null_for_originating_messages()
    {
        var interceptor = CreateInterceptor(causationId: null);
        await using var context = CreateContextWithInterceptor(interceptor);
        var ct = TestContext.Current.CancellationToken;

        var order = TestOrder.Place(Guid.NewGuid());
        context.Set<TestOrder>().Add(order);
        await context.SaveChangesAsync(ct);

        var message = context.Set<OutboxMessage>().Single();
        Assert.Null(message.CausationId);
    }

    [Fact]
    public async Task SaveChanges_writes_all_events_from_all_aggregates()
    {
        var interceptor = CreateInterceptor();
        await using var context = CreateContextWithInterceptor(interceptor);
        var ct = TestContext.Current.CancellationToken;

        var order1 = TestOrder.Place(Guid.NewGuid());
        var order2 = TestOrder.Place(Guid.NewGuid());
        context.Set<TestOrder>().AddRange(order1, order2);
        await context.SaveChangesAsync(ct);

        var outboxMessages = context.Set<OutboxMessage>().ToList();
        Assert.Equal(2, outboxMessages.Count);
    }

    [Fact]
    public async Task SaveChanges_writes_multiple_events_from_same_aggregate()
    {
        var interceptor = CreateInterceptor();
        await using var context = CreateContextWithInterceptor(interceptor);
        var ct = TestContext.Current.CancellationToken;

        var order = TestOrder.Place(Guid.NewGuid());
        order.Cancel();
        context.Set<TestOrder>().Add(order);
        await context.SaveChangesAsync(ct);

        var outboxMessages = context.Set<OutboxMessage>().ToList();
        Assert.Equal(2, outboxMessages.Count);
    }

    [Fact]
    public async Task SaveChanges_commits_aggregate_and_outbox_atomically()
    {
        var interceptor = CreateInterceptor();
        await using var context = CreateContextWithInterceptor(interceptor);
        var ct = TestContext.Current.CancellationToken;

        var id = Guid.NewGuid();
        var order = TestOrder.Place(id);
        context.Set<TestOrder>().Add(order);
        await context.SaveChangesAsync(ct);

        Assert.NotNull(await context.Set<TestOrder>().FindAsync([id], ct));
        Assert.Single(context.Set<OutboxMessage>().ToList());
    }

    [Fact]
    public async Task SaveChanges_does_nothing_for_aggregates_with_no_events()
    {
        var interceptor = CreateInterceptor();
        await using var context = CreateContextWithInterceptor(interceptor);
        var ct = TestContext.Current.CancellationToken;

        var order = new TestOrder { Id = Guid.NewGuid() };
        context.Set<TestOrder>().Add(order);
        await context.SaveChangesAsync(ct);

        Assert.Empty(context.Set<OutboxMessage>().ToList());
    }

    private sealed class FakeMessagingContext : IMessagingContext
    {
        public FakeMessagingContext(Guid correlationId, Guid? causationId)
        {
            CorrelationId = correlationId;
            CausationId = causationId;
        }

        public Guid MessageId { get; } = Guid.NewGuid();
        public Guid CorrelationId { get; }
        public Guid? CausationId { get; }
    }
}
