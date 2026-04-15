namespace OpinionatedEventing;

/// <summary>
/// Handles events of type <typeparamref name="TEvent"/>.
/// Multiple registrations for the same event type are allowed (fan-out).
/// </summary>
/// <typeparam name="TEvent">The event type to handle.</typeparam>
public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    /// <summary>Handles the given <paramref name="event"/>.</summary>
    /// <param name="event">The event to handle.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
