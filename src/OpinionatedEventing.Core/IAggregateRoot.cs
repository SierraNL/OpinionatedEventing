namespace OpinionatedEventing;

/// <summary>
/// Marks a class as a DDD aggregate root that collects domain events.
/// The EF Core interceptor (<c>DomainEventInterceptor</c>) detects this interface
/// during <c>SaveChanges</c> to harvest and outbox the accumulated events.
/// </summary>
/// <remarks>
/// Implement this interface directly when your aggregate already inherits from another
/// base class. For the common case with no existing base class, inherit from
/// <see cref="AggregateRoot"/> instead — it provides the standard implementation for free.
/// </remarks>
public interface IAggregateRoot
{
    /// <summary>Gets the domain events raised during the current unit of work.</summary>
    IReadOnlyList<IEvent> DomainEvents { get; }

    /// <summary>
    /// Clears all collected domain events.
    /// <para>
    /// <b>Framework use only.</b> This method is called by <c>DomainEventInterceptor</c>
    /// after the events have been written to the outbox. Application code must not call it.
    /// </para>
    /// </summary>
    void ClearDomainEvents();
}
