namespace OpinionatedEventing.Sagas;

/// <summary>
/// Represents the persisted state of a saga instance.
/// Stored and retrieved by <see cref="ISagaStateStore"/>.
/// </summary>
public sealed class SagaState
{
    /// <summary>Gets the unique identifier of this saga instance.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the assembly-qualified CLR type name of the orchestrator that owns this state.
    /// </summary>
    public required string SagaType { get; init; }

    /// <summary>
    /// Gets the correlation identifier that links all messages belonging to this saga instance.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>Gets or sets the JSON-serialised saga-specific state payload.</summary>
    public required string State { get; set; }

    /// <summary>Gets or sets the current lifecycle status of this saga instance.</summary>
    public SagaStatus Status { get; set; }

    /// <summary>Gets the UTC time at which this saga instance was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets or sets the UTC time at which this saga instance was last modified.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time at which this saga instance expires,
    /// or <see langword="null"/> if no timeout is configured.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time until which this saga instance is claimed by a timeout worker,
    /// or <see langword="null"/> if not currently claimed.
    /// Expired claims are re-eligible automatically, providing crash recovery.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the timeout worker instance that has claimed this saga,
    /// or <see langword="null"/> if not currently claimed.
    /// </summary>
    public string? LockedBy { get; set; }
}
