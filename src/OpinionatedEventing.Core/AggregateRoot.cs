namespace OpinionatedEventing;

/// <summary>
/// Convenience base class for DDD aggregate roots.
/// Provides the standard <see cref="IAggregateRoot"/> implementation so aggregates
/// do not need boilerplate event-collection code.
/// </summary>
/// <remarks>
/// Inherit from this class when your aggregate has no other base class requirement.
/// If you already inherit from another type, implement <see cref="IAggregateRoot"/> directly instead.
/// Aggregates must never depend on <see cref="IPublisher"/> directly.
/// </remarks>
public abstract class AggregateRoot : IAggregateRoot
{
    private readonly List<IEvent> _domainEvents = [];

    /// <inheritdoc/>
    public IReadOnlyList<IEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Records a domain event to be harvested by the outbox interceptor when
    /// <c>SaveChanges</c> is called.
    /// </summary>
    /// <param name="domainEvent">The domain event to raise.</param>
    protected void RaiseDomainEvent(IEvent domainEvent)
        => _domainEvents.Add(domainEvent);

    /// <inheritdoc/>
    /// <remarks>Explicit implementation prevents accidental calls from application code.</remarks>
    void IAggregateRoot.ClearDomainEvents()
        => _domainEvents.Clear();
}
