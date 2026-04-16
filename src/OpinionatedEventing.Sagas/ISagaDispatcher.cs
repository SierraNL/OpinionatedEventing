namespace OpinionatedEventing.Sagas;

/// <summary>
/// Routes inbound events to matching saga orchestrators and choreography participants.
/// Resolve from a DI scope; one scope per inbound message.
/// </summary>
public interface ISagaDispatcher
{
    /// <summary>
    /// Dispatches <paramref name="event"/> to all matching saga orchestrators and participants.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="event">The inbound event.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task DispatchAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
