namespace OpinionatedEventing.Sagas;

/// <summary>
/// Context available to saga handlers for sending commands, publishing events,
/// and controlling the saga lifecycle.
/// </summary>
public interface ISagaContext
{
    /// <summary>Gets the correlation identifier for the current saga instance.</summary>
    Guid CorrelationId { get; }

    /// <summary>Writes a command to the outbox.</summary>
    Task SendCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand;

    /// <summary>Writes an event to the outbox.</summary>
    Task PublishEventAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    /// <summary>
    /// Marks the saga as <see cref="SagaStatus.Completed"/>.
    /// Call at the end of the final step in the happy path, or after compensation finishes.
    /// </summary>
    void Complete();
}
