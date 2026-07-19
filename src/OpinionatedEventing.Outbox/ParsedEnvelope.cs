#nullable enable

namespace OpinionatedEventing.Outbox;

/// <summary>
/// The result of parsing a broker-native or CloudEvents envelope back into the fields required to
/// dispatch a message via <see cref="OpinionatedEventing.IMessageHandlerRunner"/>.
/// </summary>
/// <param name="MessageType">
/// Stable registry identifier of the message (CLR <c>FullName</c> or <c>[MessageType]</c> override).
/// </param>
/// <param name="MessageKind"><c>"Event"</c> or <c>"Command"</c>.</param>
/// <param name="CorrelationId">Correlation identifier propagated from the inbound envelope.</param>
/// <param name="MessageId">
/// The inbound message's own identifier, or <see langword="null"/> when the envelope carries no
/// parseable ID.
/// </param>
/// <param name="CausationId">
/// The identifier to record as the causation of any message published while handling this one,
/// or <see langword="null"/> for originating messages.
/// </param>
/// <param name="Payload">JSON-serialised message body.</param>
public sealed record ParsedEnvelope(
    string MessageType,
    string MessageKind,
    Guid CorrelationId,
    Guid? MessageId,
    Guid? CausationId,
    string Payload);
