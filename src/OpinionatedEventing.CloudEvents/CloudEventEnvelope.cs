#nullable enable

namespace OpinionatedEventing.CloudEvents;

/// <summary>
/// The result of parsing a CloudEvents 1.0 structured JSON envelope via
/// <see cref="CloudEventsEnvelopeMapper.Deserialize(string)"/>.
/// </summary>
/// <param name="Id">The CloudEvents <c>id</c> attribute — the originating <c>OutboxMessage.Id</c>.</param>
/// <param name="Type">The CloudEvents <c>type</c> attribute.</param>
/// <param name="Source">The CloudEvents <c>source</c> attribute.</param>
/// <param name="Time">The CloudEvents <c>time</c> attribute.</param>
/// <param name="Data">The raw JSON of the CloudEvents <c>data</c> attribute — the message payload.</param>
/// <param name="CorrelationId">The <c>correlationid</c> extension attribute.</param>
/// <param name="CausationId">The <c>causationid</c> extension attribute, or <see langword="null"/> if absent.</param>
public sealed record CloudEventEnvelope(
    Guid Id,
    string Type,
    Uri Source,
    DateTimeOffset Time,
    string Data,
    Guid CorrelationId,
    Guid? CausationId);
