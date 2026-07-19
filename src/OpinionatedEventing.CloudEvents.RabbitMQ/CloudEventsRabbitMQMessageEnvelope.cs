#nullable enable

using System.Text;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.RabbitMQ;
using RabbitMQ.Client;

namespace OpinionatedEventing.CloudEvents.RabbitMQ;

/// <summary>
/// <see cref="IRabbitMQMessageEnvelope"/> decorator that wraps events in a CloudEvents 1.0
/// structured envelope (<see cref="CloudEventsEnvelopeMapper.ContentType"/>) while leaving commands
/// in the broker-native format produced by the wrapped <see cref="DefaultRabbitMQMessageEnvelope"/>.
/// </summary>
public sealed class CloudEventsRabbitMQMessageEnvelope : IRabbitMQMessageEnvelope
{
    private readonly DefaultRabbitMQMessageEnvelope _inner;
    private readonly IOptions<CloudEventsOptions> _options;

    /// <summary>Initialises a new <see cref="CloudEventsRabbitMQMessageEnvelope"/>.</summary>
    public CloudEventsRabbitMQMessageEnvelope(
        DefaultRabbitMQMessageEnvelope inner, IOptions<CloudEventsOptions> options)
    {
        _inner = inner;
        _options = options;
    }

    /// <inheritdoc/>
    public RabbitMQMessageEnvelopeResult CreateMessage(OutboxMessage message)
    {
        if (message.MessageKind != MessageKind.Event)
            return _inner.CreateMessage(message);

        var json = CloudEventsEnvelopeMapper.Serialize(message, _options.Value);
        ReadOnlyMemory<byte> body = Encoding.UTF8.GetBytes(json);
        BasicProperties properties = new()
        {
            MessageId = message.Id.ToString(),
            ContentType = CloudEventsEnvelopeMapper.ContentType,
            CorrelationId = message.CorrelationId.ToString(),
            DeliveryMode = DeliveryModes.Persistent,
            // Must be non-null: the consumer's retry/dead-letter path copies Headers
            // unconditionally when a handler throws, regardless of which envelope published
            // the message (see IRabbitMQMessageEnvelope.CreateMessage).
            Headers = new Dictionary<string, object?>(),
        };

        return new RabbitMQMessageEnvelopeResult(properties, body);
    }

    /// <inheritdoc/>
    public ParsedEnvelope Parse(IReadOnlyBasicProperties properties, ReadOnlyMemory<byte> body)
    {
        if (properties.ContentType != CloudEventsEnvelopeMapper.ContentType)
            return _inner.Parse(properties, body);

        try
        {
            var cloudEvent = CloudEventsEnvelopeMapper.Deserialize(Encoding.UTF8.GetString(body.Span));
            return new ParsedEnvelope(
                cloudEvent.Type, nameof(MessageKind.Event), cloudEvent.CorrelationId,
                cloudEvent.Id, cloudEvent.CausationId, cloudEvent.Data);
        }
        catch (FormatException ex)
        {
            throw new MessageEnvelopeFormatException("InvalidMessageFormat", ex.Message);
        }
    }
}
