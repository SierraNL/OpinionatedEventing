#nullable enable

namespace OpinionatedEventing.Testing;

/// <summary>
/// Configurable implementation of <see cref="IMessagingContext"/> for use in unit tests.
/// Allows tests to set fixed <see cref="MessageId"/>, <see cref="CorrelationId"/>, and
/// <see cref="CausationId"/> values instead of relying on transport-populated values.
/// Not for production use.
/// </summary>
public sealed class FakeMessagingContext : IMessagingContext
{
    /// <summary>Gets or sets the message identifier returned by this context.</summary>
    public Guid MessageId { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the correlation identifier returned by this context.</summary>
    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the causation identifier returned by this context.</summary>
    public Guid? CausationId { get; set; }
}
