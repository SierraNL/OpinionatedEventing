#nullable enable

using Azure.Messaging.ServiceBus;
using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.AzureServiceBus;

/// <summary>
/// Built-in <see cref="IServiceBusMessageEnvelope"/> implementation. Carries <c>MessageType</c>,
/// <c>MessageKind</c>, <c>CorrelationId</c>, and <c>CausationId</c> as application properties, with
/// the raw JSON payload as the message body.
/// </summary>
public sealed class DefaultServiceBusMessageEnvelope : IServiceBusMessageEnvelope
{
    /// <inheritdoc/>
    public ServiceBusMessage CreateMessage(OutboxMessage message)
    {
        var sbMessage = new ServiceBusMessage(BinaryData.FromString(message.Payload))
        {
            MessageId = message.Id.ToString(),
            ContentType = "application/json",
            CorrelationId = message.CorrelationId.ToString(),
        };
        sbMessage.ApplicationProperties["MessageType"] = message.MessageType;
        sbMessage.ApplicationProperties["MessageKind"] = message.MessageKind.ToString();
        sbMessage.ApplicationProperties["CorrelationId"] = message.CorrelationId.ToString();
        if (message.CausationId.HasValue)
            sbMessage.ApplicationProperties["CausationId"] = message.CausationId.Value.ToString();

        return sbMessage;
    }

    /// <inheritdoc/>
    public ParsedEnvelope Parse(ServiceBusReceivedMessage message)
    {
        var messageType = message.ApplicationProperties.TryGetValue("MessageType", out var mt)
            ? mt as string : null;
        var messageKind = message.ApplicationProperties.TryGetValue("MessageKind", out var mk)
            ? mk as string : null;
        var correlationIdStr = message.ApplicationProperties.TryGetValue("CorrelationId", out var cid)
            ? cid as string : null;

        if (messageType is null || messageKind is null || correlationIdStr is null)
            throw new MessageEnvelopeFormatException(
                "InvalidMessageFormat", "Missing required application properties.");

        if (!Guid.TryParse(correlationIdStr, out var correlationId))
            throw new MessageEnvelopeFormatException(
                "InvalidMessageFormat", "CorrelationId is not a valid Guid.");

        Guid? messageId = Guid.TryParse(message.MessageId, out var mid) ? mid : null;
        Guid? causationId = messageId;
        var payload = message.Body.ToString();

        return new ParsedEnvelope(messageType, messageKind, correlationId, messageId, causationId, payload);
    }
}
