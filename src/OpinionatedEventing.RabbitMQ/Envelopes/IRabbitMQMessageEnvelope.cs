#nullable enable

using OpinionatedEventing.Outbox;
using RabbitMQ.Client;

namespace OpinionatedEventing.RabbitMQ;

/// <summary>
/// Builds and parses the RabbitMQ wire representation of an outbox message. Registered as a
/// singleton; <see cref="DefaultRabbitMQMessageEnvelope"/> is the built-in broker-native behaviour.
/// Replace the registration (e.g. via <c>UseCloudEventsEnvelope()</c>) to change how messages are
/// represented on the wire without touching <see cref="RabbitMQTransport"/> or
/// <see cref="RabbitMQConsumerWorker"/>.
/// </summary>
public interface IRabbitMQMessageEnvelope
{
    /// <summary>
    /// Builds the outbound message properties and body for <paramref name="message"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="RabbitMQMessageEnvelopeResult.Properties"/> must always carry a non-null
    /// <see cref="BasicProperties.Headers"/> dictionary: the consumer worker's retry/dead-letter
    /// path copies it when a handler throws, regardless of which envelope produced the message.
    /// </remarks>
    RabbitMQMessageEnvelopeResult CreateMessage(OutboxMessage message);

    /// <summary>
    /// Parses inbound message properties and body back into a <see cref="ParsedEnvelope"/>.
    /// </summary>
    /// <exception cref="MessageEnvelopeFormatException">
    /// The message is missing required data or is otherwise malformed.
    /// </exception>
    ParsedEnvelope Parse(IReadOnlyBasicProperties properties, ReadOnlyMemory<byte> body);
}

/// <summary>The outbound message properties and body produced by an <see cref="IRabbitMQMessageEnvelope"/>.</summary>
/// <param name="Properties">The AMQP basic properties, including a non-null <c>Headers</c> dictionary.</param>
/// <param name="Body">The message body.</param>
public sealed record RabbitMQMessageEnvelopeResult(BasicProperties Properties, ReadOnlyMemory<byte> Body);
