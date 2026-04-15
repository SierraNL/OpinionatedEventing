namespace OpinionatedEventing;

/// <summary>
/// Base class for DDD aggregate roots.
/// Aggregates collect domain events via <see cref="RaiseDomainEvent"/> during business operations.
/// The EF Core interceptor (<c>DomainEventInterceptor</c>) harvests these events and writes them
/// to the outbox atomically within the same <c>SaveChanges</c> call.
/// Aggregates must never depend on <see cref="IPublisher"/> directly.
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IEvent> _domainEvents = [];

    /// <summary>Gets the domain events raised during this aggregate's current unit of work.</summary>
    public IReadOnlyList<IEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Records a domain event to be harvested by the outbox interceptor when
    /// <c>SaveChanges</c> is called.
    /// </summary>
    /// <param name="domainEvent">The domain event to raise.</param>
    protected void RaiseDomainEvent(IEvent domainEvent)
        => _domainEvents.Add(domainEvent);

    /// <summary>
    /// Clears all collected domain events. Called by <c>DomainEventInterceptor</c>
    /// after the events have been written to the outbox.
    /// </summary>
    internal void ClearDomainEvents()
        => _domainEvents.Clear();
}
