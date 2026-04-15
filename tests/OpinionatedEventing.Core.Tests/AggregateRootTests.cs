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

        ((IAggregateRoot)aggregate).ClearDomainEvents();

        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void DomainEvents_ReturnsReadOnlyView()
    {
        var aggregate = new TestAggregate();
        aggregate.Place(Guid.NewGuid());

        Assert.IsAssignableFrom<IReadOnlyList<IEvent>>(aggregate.DomainEvents);
    }

    [Fact]
    public void AggregateRoot_ImplementsIAggregateRoot()
    {
        var aggregate = new TestAggregate();
        Assert.IsAssignableFrom<IAggregateRoot>(aggregate);
    }

    [Fact]
    public void ManualIAggregateRootImplementation_WorksWithoutBaseClass()
    {
        // Verify a hand-written IAggregateRoot (no base class) behaves the same way.
        var aggregate = new ManualAggregate();
        var id = Guid.NewGuid();

        aggregate.Place(id);
        Assert.Single(aggregate.DomainEvents);

        ((IAggregateRoot)aggregate).ClearDomainEvents();
        Assert.Empty(aggregate.DomainEvents);
    }

    // A hand-rolled aggregate that does NOT inherit AggregateRoot.
    private sealed class ManualAggregate : IAggregateRoot
    {
        private readonly List<IEvent> _events = [];
        public IReadOnlyList<IEvent> DomainEvents => _events.AsReadOnly();
        public void Place(Guid id) => _events.Add(new OrderPlaced(id));
        void IAggregateRoot.ClearDomainEvents() => _events.Clear();
    }
}
