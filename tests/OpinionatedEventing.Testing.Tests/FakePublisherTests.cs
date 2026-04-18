using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Tests;

public sealed class FakePublisherTests
{
    public sealed record OrderPlaced(Guid OrderId) : IEvent;
    public sealed record ProcessPayment(Guid OrderId) : ICommand;

    [Fact]
    public async Task PublishEventAsync_RecordsEvent()
    {
        var publisher = new FakePublisher();
        await publisher.PublishEventAsync(new OrderPlaced(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Single(publisher.PublishedEvents);
        Assert.IsType<OrderPlaced>(publisher.PublishedEvents[0]);
    }

    [Fact]
    public async Task SendCommandAsync_RecordsCommand()
    {
        var publisher = new FakePublisher();
        await publisher.SendCommandAsync(new ProcessPayment(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Single(publisher.SentCommands);
        Assert.IsType<ProcessPayment>(publisher.SentCommands[0]);
    }

    [Fact]
    public async Task PublishedEvents_PreservesOrder()
    {
        var publisher = new FakePublisher();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        await publisher.PublishEventAsync(new OrderPlaced(id1), TestContext.Current.CancellationToken);
        await publisher.PublishEventAsync(new OrderPlaced(id2), TestContext.Current.CancellationToken);

        Assert.Equal(id1, ((OrderPlaced)publisher.PublishedEvents[0]).OrderId);
        Assert.Equal(id2, ((OrderPlaced)publisher.PublishedEvents[1]).OrderId);
    }

    [Fact]
    public async Task SentCommands_AndPublishedEvents_AreIndependent()
    {
        var publisher = new FakePublisher();
        await publisher.PublishEventAsync(new OrderPlaced(Guid.NewGuid()), TestContext.Current.CancellationToken);
        await publisher.SendCommandAsync(new ProcessPayment(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Single(publisher.PublishedEvents);
        Assert.Single(publisher.SentCommands);
    }
}
