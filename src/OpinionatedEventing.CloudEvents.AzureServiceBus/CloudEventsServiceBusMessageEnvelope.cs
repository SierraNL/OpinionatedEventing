#nullable enable

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using OpinionatedEventing.AzureServiceBus;
using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.CloudEvents.AzureServiceBus;

/// <summary>
/// <see cref="IServiceBusMessageEnvelope"/> decorator that wraps events in a CloudEvents 1.0
/// structured envelope (<see cref="CloudEventsEnvelopeMapper.ContentType"/>) while leaving commands
/// in the broker-native format produced by the wrapped <see cref="DefaultServiceBusMessageEnvelope"/>.
/// </summary>
public sealed class CloudEventsServiceBusMessageEnvelope : IServiceBusMessageEnvelope
{
    private readonly DefaultServiceBusMessageEnvelope _inner;
    private readonly IOptions<CloudEventsOptions> _options;

    /// <summary>Initialises a new <see cref="CloudEventsServiceBusMessageEnvelope"/>.</summary>
    public CloudEventsServiceBusMessageEnvelope(
        DefaultServiceBusMessageEnvelope inner, IOptions<CloudEventsOptions> options)
    {
        _inner = inner;
        _options = options;
    }

    /// <inheritdoc/>
    public ServiceBusMessage CreateMessage(OutboxMessage message)
    {
        if (message.MessageKind != MessageKind.Event)
            return _inner.CreateMessage(message);

        var json = CloudEventsEnvelopeMapper.Serialize(message, _options.Value);
        return new ServiceBusMessage(BinaryData.FromString(json))
        {
            MessageId = message.Id.ToString(),
            ContentType = CloudEventsEnvelopeMapper.ContentType,
            CorrelationId = message.CorrelationId.ToString(),
        };
    }

    /// <inheritdoc/>
    public ParsedEnvelope Parse(ServiceBusReceivedMessage message)
    {
        if (message.ContentType != CloudEventsEnvelopeMapper.ContentType)
            return _inner.Parse(message);

        try
        {
            var cloudEvent = CloudEventsEnvelopeMapper.Deserialize(message.Body.ToString());
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
