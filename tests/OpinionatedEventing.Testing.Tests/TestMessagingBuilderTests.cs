using Microsoft.Extensions.DependencyInjection;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Tests;

public sealed class TestMessagingBuilderTests
{
    public sealed record ItemShipped(Guid OrderId) : IEvent;
    public sealed record ShipOrder(Guid OrderId) : ICommand;

    private sealed class ItemShippedHandler : IEventHandler<ItemShipped>
    {
        public Task HandleAsync(ItemShipped @event, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class ShipOrderHandler : ICommandHandler<ShipOrder>
    {
        public Task HandleAsync(ShipOrder command, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [Fact]
    public void Build_ReturnsServiceProvider()
    {
        var provider = new TestMessagingBuilder().Build();
        Assert.NotNull(provider);
    }

    [Fact]
    public void Build_ResolvesIPublisherAsFakePublisher()
    {
        var builder = new TestMessagingBuilder();
        var provider = builder.Build();

        var publisher = provider.GetRequiredService<IPublisher>();
        Assert.Same(builder.Publisher, publisher);
    }

    [Fact]
    public void Build_ResolvesIMessagingContextAsFakeMessagingContext()
    {
        var builder = new TestMessagingBuilder();
        var provider = builder.Build();

        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<IMessagingContext>();
        Assert.Same(builder.MessagingContext, ctx);
    }

    [Fact]
    public void Build_ResolvesIOutboxStoreAsInMemoryOutboxStore()
    {
        var builder = new TestMessagingBuilder();
        var provider = builder.Build();

        var store = provider.GetRequiredService<IOutboxStore>();
        Assert.Same(builder.OutboxStore, store);
    }

    [Fact]
    public void AddHandlersFromAssemblies_RegistersEventHandler()
    {
        var provider = new TestMessagingBuilder()
            .AddHandlersFromAssemblies(typeof(TestMessagingBuilderTests).Assembly)
            .Build();

        using var scope = provider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IEventHandler<ItemShipped>>();
        Assert.Single(handlers);
    }

    [Fact]
    public void AddHandlersFromAssemblies_RegistersCommandHandler()
    {
        var provider = new TestMessagingBuilder()
            .AddHandlersFromAssemblies(typeof(TestMessagingBuilderTests).Assembly)
            .Build();

        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetService<ICommandHandler<ShipOrder>>();
        Assert.NotNull(handler);
    }

    [Fact]
    public void Build_ThrowsIfCalledTwice()
    {
        var builder = new TestMessagingBuilder();
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void AddHandlersFromAssemblies_IsChainable()
    {
        var builder = new TestMessagingBuilder();
        var result = builder.AddHandlersFromAssemblies(typeof(TestMessagingBuilderTests).Assembly);
        Assert.Same(builder, result);
    }

    [Fact]
    public async Task Publisher_CapturesPublishedEvents()
    {
        var builder = new TestMessagingBuilder();
        await builder.Publisher.PublishEventAsync(new ItemShipped(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Single(builder.Publisher.PublishedEvents);
    }

    [Fact]
    public void MessagingContext_AllowsFixedCorrelationId()
    {
        var id = Guid.NewGuid();
        var builder = new TestMessagingBuilder();
        builder.MessagingContext.CorrelationId = id;

        var provider = builder.Build();
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<IMessagingContext>();

        Assert.Equal(id, ctx.CorrelationId);
    }
}
