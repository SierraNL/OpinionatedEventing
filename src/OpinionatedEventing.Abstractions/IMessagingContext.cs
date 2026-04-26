namespace OpinionatedEventing;

/// <summary>
/// Carries the ambient correlation and causation identifiers for the current handler scope.
/// Registered as a scoped DI service. The transport layer populates these values when
/// an inbound message is received; the outbox interceptor reads them when stamping new outbox rows.
/// </summary>
public interface IMessagingContext
{
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
