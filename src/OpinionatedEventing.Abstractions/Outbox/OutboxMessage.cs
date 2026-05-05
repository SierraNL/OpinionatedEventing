namespace OpinionatedEventing.Outbox;

/// <summary>
/// Represents a message stored in the outbox pending delivery to the broker.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Gets the unique identifier of this outbox entry.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the assembly-qualified CLR type name of the message.
    /// Used to deserialise the payload back to the correct type on dispatch.
    /// </summary>
    public required string MessageType { get; init; }

    /// <summary>Gets the JSON-serialised message body.</summary>
    public required string Payload { get; init; }

    /// <summary>Gets the kind of message: <see cref="Outbox.MessageKind.Event"/> or <see cref="Outbox.MessageKind.Command"/>.</summary>
    public required MessageKind MessageKind { get; init; }

    /// <summary>
    /// Gets the correlation identifier propagated across the entire message chain.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// Gets the identifier of the outbox message that caused this one to be created,
    /// or <see langword="null"/> if this is an originating message.
    /// </summary>
    public Guid? CausationId { get; init; }

    /// <summary>Gets the UTC time at which this message was written to the outbox.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets or sets the UTC time at which this message was successfully delivered to the broker,
    /// or <see langword="null"/> if not yet processed.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time at which this message was permanently dead-lettered,
    /// or <see langword="null"/> if not yet failed.
    /// </summary>
    public DateTimeOffset? FailedAt { get; set; }

    /// <summary>Gets or sets the number of dispatch attempts made for this message.</summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Gets or sets the error detail recorded on the last failed dispatch attempt,
    /// or <see langword="null"/> if no failure has occurred.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the UTC time until which this message is claimed by a dispatcher instance,
    /// or <see langword="null"/> if the message is not currently claimed.
    /// Expired claims (where <c>LockedUntil &lt; UtcNow</c>) are treated as unclaimed and
    /// become eligible for re-dispatch automatically.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the dispatcher instance that has claimed this message,
    /// or <see langword="null"/> if the message is not currently claimed.
    /// </summary>
    public string? LockedBy { get; set; }

    /// <summary>
    /// Gets or sets the earliest UTC time at which this message is eligible for the next dispatch
    /// attempt, or <see langword="null"/> if the message is immediately eligible.
    /// Set by <see cref="IOutboxStore.IncrementAttemptAsync"/> to implement exponential backoff.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; set; }
}
