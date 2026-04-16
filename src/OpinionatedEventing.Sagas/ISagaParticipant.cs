namespace OpinionatedEventing.Sagas;

/// <summary>
/// Lightweight choreography participant that reacts to a single event type without holding
/// central state. Register via <c>services.AddSagaParticipant&lt;TParticipant&gt;()</c>.
/// </summary>
/// <typeparam name="TEvent">The event type to handle.</typeparam>
public interface ISagaParticipant<TEvent> where TEvent : IEvent
{
    /// <summary>Handles the event.</summary>
    /// <param name="event">The inbound event.</param>
    /// <param name="ctx">The saga context for sending commands or publishing events.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task HandleAsync(TEvent @event, ISagaContext ctx, CancellationToken cancellationToken);
}
