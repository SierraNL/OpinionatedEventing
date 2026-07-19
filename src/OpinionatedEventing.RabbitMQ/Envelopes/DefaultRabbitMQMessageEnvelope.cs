#nullable enable

using System.Text;
using OpinionatedEventing.Outbox;
using RabbitMQ.Client;

namespace OpinionatedEventing.RabbitMQ;

/// <summary>
/// Built-in <see cref="IRabbitMQMessageEnvelope"/> implementation. Carries <c>MessageType</c>,
/// <c>MessageKind</c>, <c>CorrelationId</c>, and <c>CausationId</c> as AMQP headers, with the raw
/// JSON payload as the message body.
/// </summary>
public sealed class DefaultRabbitMQMessageEnvelope : IRabbitMQMessageEnvelope
{
    /// <inheritdoc/>
    public RabbitMQMessageEnvelopeResult CreateMessage(OutboxMessage message)
    {
        ReadOnlyMemory<byte> body = Encoding.UTF8.GetBytes(message.Payload);
        BasicProperties properties = new()
        {
            MessageId = message.Id.ToString(),
            ContentType = "application/json",
            CorrelationId = message.CorrelationId.ToString(),
            DeliveryMode = DeliveryModes.Persistent,
            Headers = new Dictionary<string, object?>
            {
                ["MessageType"] = message.MessageType,
                ["MessageKind"] = message.MessageKind.ToString(),
                ["CorrelationId"] = message.CorrelationId.ToString(),
            },
        };
        if (message.CausationId.HasValue)
            properties.Headers["CausationId"] = message.CausationId.Value.ToString();

        return new RabbitMQMessageEnvelopeResult(properties, body);
    }

    /// <inheritdoc/>
    public ParsedEnvelope Parse(IReadOnlyBasicProperties properties, ReadOnlyMemory<byte> body)
    {
        var messageType = GetHeader(properties, "MessageType");
        var messageKind = GetHeader(properties, "MessageKind");
        var correlationIdStr = GetHeader(properties, "CorrelationId");

        if (messageType is null || messageKind is null || correlationIdStr is null)
            throw new MessageEnvelopeFormatException(
                "InvalidMessageFormat", "Missing required headers.");

        if (!Guid.TryParse(correlationIdStr, out var correlationId))
            throw new MessageEnvelopeFormatException(
                "InvalidMessageFormat", "CorrelationId is not a valid Guid.");

        Guid? messageId = Guid.TryParse(properties.MessageId, out var mid) ? mid : null;
        Guid? causationId = messageId;
        var payload = Encoding.UTF8.GetString(body.Span);

        return new ParsedEnvelope(messageType, messageKind, correlationId, messageId, causationId, payload);
    }

    private static string? GetHeader(IReadOnlyBasicProperties properties, string key)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(key, out var value))
            return null;
        return value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value?.ToString();
    }
}
