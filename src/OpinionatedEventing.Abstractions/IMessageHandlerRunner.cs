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
    /// <paramref name="messageId"/>, <paramref name="correlationId"/>, and
    /// <paramref name="causationId"/>, deserialises <paramref name="payload"/>, and invokes
    /// all matching <see cref="IEventHandler{TEvent}"/> or the single
    /// <see cref="ICommandHandler{TCommand}"/>.
    /// </summary>
    /// <param name="messageType">
    /// Stable registry identifier of the message (CLR <c>FullName</c> or
    /// <c>[MessageType]</c> override). The legacy <c>AssemblyQualifiedName</c> format is also
    /// accepted for backwards-compatibility with existing outbox rows.
    /// </param>
    /// <param name="messageKind"><c>"Event"</c> or <c>"Command"</c>.</param>
    /// <param name="payload">JSON-serialised message body.</param>
    /// <param name="messageId">
    /// The inbound message's own identifier. Exposed via <see cref="IMessagingContext.MessageId"/>
    /// so handlers can use it as an idempotency key. Pass <see langword="null"/> when the
    /// inbound envelope carries no parseable ID; a new <see cref="Guid"/> will be generated.
    /// </param>
    /// <param name="correlationId">Correlation identifier propagated from the inbound envelope.</param>
    /// <param name="causationId">
    /// The inbound message's own identifier (its <c>MessageId</c> on the wire). Any message
    /// published during handling will carry this value as its <c>CausationId</c>, establishing the
    /// direct parent–child link. Pass <see langword="null"/> for originating messages that have no
    /// parseable <c>MessageId</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task RunAsync(
        string messageType,
        string messageKind,
        string payload,
        Guid? messageId,
        Guid correlationId,
        Guid? causationId,
        CancellationToken ct);
}
