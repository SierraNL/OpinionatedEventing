#nullable enable

namespace OpinionatedEventing.Outbox;

/// <summary>
/// Thrown by an <c>IServiceBusMessageEnvelope</c> or <c>IRabbitMQMessageEnvelope</c> implementation
/// when an inbound broker message cannot be parsed into a <see cref="ParsedEnvelope"/>, for example
/// because required properties are missing or a CloudEvents structured envelope is malformed.
/// Transport consumer workers catch this exception to dead-letter the message with
/// <see cref="Reason"/> and <see cref="Description"/> instead of attempting to run a handler.
/// </summary>
public sealed class MessageEnvelopeFormatException : Exception
{
    /// <summary>Gets the short, machine-readable dead-letter reason.</summary>
    public string Reason { get; }

    /// <summary>Gets the human-readable description of the parse failure.</summary>
    public string Description { get; }

    /// <summary>Initialises a new <see cref="MessageEnvelopeFormatException"/>.</summary>
    public MessageEnvelopeFormatException(string reason, string description)
        : base(description)
    {
        Reason = reason;
        Description = description;
    }
}
