namespace OpinionatedEventing;

/// <summary>
/// Default scoped implementation of <see cref="IMessagingContext"/>.
/// Transport packages resolve this concrete type to populate correlation values
/// before dispatching to handlers.
/// </summary>
public sealed class MessagingContext : IMessagingContext
{
    /// <inheritdoc/>
    public Guid CorrelationId { get; private set; } = Guid.NewGuid();

    /// <inheritdoc/>
    public Guid? CausationId { get; private set; }

    /// <summary>
    /// Initialises the context with values propagated from an inbound message.
    /// Called by the transport layer at the start of each handler scope.
    /// </summary>
    /// <param name="correlationId">The correlation identifier from the inbound message.</param>
    /// <param name="causationId">The outbox message identifier that caused this scope.</param>
    public void Initialize(Guid correlationId, Guid? causationId)
    {
        CorrelationId = correlationId;
        CausationId = causationId;
    }
}
