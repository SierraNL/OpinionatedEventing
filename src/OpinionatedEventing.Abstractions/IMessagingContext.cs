namespace OpinionatedEventing;

/// <summary>
/// Carries the ambient message, correlation, and causation identifiers for the current handler scope.
/// Registered as a scoped DI service. The transport layer populates these values when
/// an inbound message is received; the outbox interceptor reads them when stamping new outbox rows.
/// </summary>
public interface IMessagingContext
{
    /// <summary>
    /// Gets the identifier of the inbound message that initiated the current handler scope,
    /// or a newly-generated <see cref="Guid"/> for originating messages that carry no parseable ID.
    /// Use this value as an idempotency key instead of embedding a separate ID in every event record.
    /// </summary>
    Guid MessageId { get; }

    /// <summary>
    /// Gets the correlation identifier propagated through the entire message chain.
    /// A new <see cref="Guid"/> is assigned when no inbound correlation context is present.
    /// </summary>
    Guid CorrelationId { get; }

    /// <summary>
    /// Gets the identifier of the outbox message that triggered the current handler scope,
    /// or <see langword="null"/> for originating messages.
    /// </summary>
    Guid? CausationId { get; }
}
