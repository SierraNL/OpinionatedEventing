using Xunit;

namespace OpinionatedEventing.Tests;

public sealed class AggregateRootTests
{
    private sealed record OrderPlaced(Guid OrderId) : IEvent;
    private sealed record OrderCancelled(Guid OrderId, string Reason) : IEvent;

    private sealed class TestAggregate : AggregateRoot
    {
        public void Place(Guid id) => RaiseDomainEvent(new OrderPlaced(id));
        public void Cancel(Guid id, string reason) => RaiseDomainEvent(new OrderCancelled(id, reason));
    }

    [Fact]
    public void DomainEvents_IsEmpty_WhenNoEventsRaised()
    {
        var aggregate = new TestAggregate();
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void RaiseDomainEvent_AppendsEventInOrder()
    {
        var aggregate = new TestAggregate();
        var id = Guid.NewGuid();

        aggregate.Place(id);
        aggregate.Cancel(id, "Changed mind");

        Assert.Equal(2, aggregate.DomainEvents.Count);
        Assert.IsType<OrderPlaced>(aggregate.DomainEvents[0]);
        Assert.IsType<OrderCancelled>(aggregate.DomainEvents[1]);
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var aggregate = new TestAggregate();
        aggregate.Place(Guid.NewGuid());
        aggregate.Place(Guid.NewGuid());

        aggregate.ClearDomainEvents();

        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void DomainEvents_ReturnsReadOnlyView()
    {
        var aggregate = new TestAggregate();
        aggregate.Place(Guid.NewGuid());

        Assert.IsAssignableFrom<IReadOnlyList<IEvent>>(aggregate.DomainEvents);
    }
}
