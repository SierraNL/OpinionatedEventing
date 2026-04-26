#nullable enable

namespace OpinionatedEventing;

/// <summary>
/// Dispatches a deserialized inbound message to its registered handler(s), initialising
/// <see cref="IMessagingContext"/> from the message envelope before any handler runs.
/// </summary>
/// <remarks>
/// Transport implementations resolve this service when a message is received from the broker.
/// A new DI scope is created per dispatch so that handler dependencies (including
/// <see cref="IMessagingContext"/>) are scoped to the lifetime of a single message.
/// </remarks>
public interface IMessageHandlerRunner
{
    /// <summary>
    /// Creates a new DI scope, initialises <see cref="IMessagingContext"/> with
    /// <paramref name="correlationId"/> and <paramref name="causationId"/>, deserialises
    /// <paramref name="payload"/>, and invokes all matching
    /// <see cref="IEventHandler{TEvent}"/> or the single <see cref="ICommandHandler{TCommand}"/>.
    /// </summary>
    /// <param name="messageType">Assembly-qualified CLR type name of the message.</param>
    /// <param name="messageKind"><c>"Event"</c> or <c>"Command"</c>.</param>
    /// <param name="payload">JSON-serialised message body.</param>
    /// <param name="correlationId">Correlation identifier propagated from the inbound envelope.</param>
    /// <param name="causationId">
    /// Identifier of the outbox message that caused this one, or <see langword="null"/> for
    /// originating messages.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task RunAsync(
        string messageType,
        string messageKind,
        string payload,
        Guid correlationId,
        Guid? causationId,
        CancellationToken ct);
}
