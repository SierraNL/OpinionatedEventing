using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Tests;

public sealed class FakeSagaContextTests
{
    public sealed record OrderPlaced(Guid OrderId) : IEvent;
    public sealed record ProcessPayment(Guid OrderId) : ICommand;

    [Fact]
    public void CorrelationId_DefaultsToNonEmptyGuid()
    {
        var ctx = new FakeSagaContext();

        Assert.NotEqual(Guid.Empty, ctx.CorrelationId);
    }

    [Fact]
    public void CorrelationId_CanBeSetExplicitly()
    {
        var id = Guid.NewGuid();
        var ctx = new FakeSagaContext { CorrelationId = id };

        Assert.Equal(id, ctx.CorrelationId);
    }

    [Fact]
    public async Task PublishEventAsync_RecordsEvent()
    {
        var ctx = new FakeSagaContext();
        await ctx.PublishEventAsync(new OrderPlaced(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Single(ctx.PublishedEvents);
        Assert.IsType<OrderPlaced>(ctx.PublishedEvents[0]);
    }

    [Fact]
    public async Task SendCommandAsync_RecordsCommand()
    {
        var ctx = new FakeSagaContext();
        await ctx.SendCommandAsync(new ProcessPayment(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Single(ctx.SentCommands);
        Assert.IsType<ProcessPayment>(ctx.SentCommands[0]);
    }

    [Fact]
    public async Task PublishedEvents_PreservesOrder()
    {
        var ctx = new FakeSagaContext();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        await ctx.PublishEventAsync(new OrderPlaced(id1), TestContext.Current.CancellationToken);
        await ctx.PublishEventAsync(new OrderPlaced(id2), TestContext.Current.CancellationToken);

        Assert.Equal(id1, ((OrderPlaced)ctx.PublishedEvents[0]).OrderId);
        Assert.Equal(id2, ((OrderPlaced)ctx.PublishedEvents[1]).OrderId);
    }

    [Fact]
    public async Task SentCommands_AndPublishedEvents_AreIndependent()
    {
        var ctx = new FakeSagaContext();
        await ctx.PublishEventAsync(new OrderPlaced(Guid.NewGuid()), TestContext.Current.CancellationToken);
        await ctx.SendCommandAsync(new ProcessPayment(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Single(ctx.PublishedEvents);
        Assert.Single(ctx.SentCommands);
    }

    [Fact]
    public void IsCompleted_IsFalseByDefault()
    {
        var ctx = new FakeSagaContext();

        Assert.False(ctx.IsCompleted);
    }

    [Fact]
    public void Complete_SetsIsCompletedTrue()
    {
        var ctx = new FakeSagaContext();
        ctx.Complete();

        Assert.True(ctx.IsCompleted);
    }
}
