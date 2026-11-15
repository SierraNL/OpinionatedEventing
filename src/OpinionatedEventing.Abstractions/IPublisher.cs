namespace OpinionatedEventing;

/// <summary>
/// The only outbound message path in OpinionatedEventing.
/// All calls write to the outbox — no message is sent directly to a broker.
/// </summary>
/// <remarks>
/// Call within the same unit of work as your state change and before <c>SaveChangesAsync</c>, so the outbox
/// row commits atomically with it. Do not bypass this with a broker client; do not publish domain events from
/// aggregates through this interface (raise them on the aggregate root instead). Requires <c>AddOutbox()</c>.
/// </remarks>
/// <example>
/// <code>
/// db.Orders.Add(order);
/// await publisher.PublishEventAsync(new OrderPlaced(order.Id), ct);
/// await db.SaveChangesAsync(ct); // outbox row commits with the order
/// </code>
/// </example>
public interface IPublisher
{
    /// <summary>
    /// Writes an event to the outbox. Broker delivery is performed asynchronously
    /// by <c>OutboxDispatcherWorker</c>.
    /// </summary>
    /// <typeparam name="TEvent">The event type. Must implement <see cref="IEvent"/>.</typeparam>
    /// <param name="event">The event to publish.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task PublishEventAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    /// <summary>
    /// Writes a command to the outbox. Broker delivery is performed asynchronously
    /// by <c>OutboxDispatcherWorker</c>.
    /// </summary>
    /// <typeparam name="TCommand">The command type. Must implement <see cref="ICommand"/>.</typeparam>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task SendCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand;
}
