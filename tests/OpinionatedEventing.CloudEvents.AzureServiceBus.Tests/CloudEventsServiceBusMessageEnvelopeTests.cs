#nullable enable

using Azure.Messaging.ServiceBus;
using MSOptions = Microsoft.Extensions.Options.Options;
using OpinionatedEventing.AzureServiceBus;
using OpinionatedEventing.CloudEvents;
using OpinionatedEventing.CloudEvents.AzureServiceBus;
using OpinionatedEventing.Outbox;
using Xunit;

namespace OpinionatedEventing.CloudEvents.AzureServiceBus.Tests;

public sealed class CloudEventsServiceBusMessageEnvelopeTests
{
    private static readonly Uri Source = new("urn:order-service");

    private static CloudEventsServiceBusMessageEnvelope CreateEnvelope(Func<OutboxMessage, string>? typeFormatter = null)
        => new(new DefaultServiceBusMessageEnvelope(),
            MSOptions.Create(new CloudEventsOptions { Source = Source, TypeFormatter = typeFormatter }));

    private static OutboxMessage BuildMessage(MessageKind kind, Guid? causationId = null) => new()
    {
        Id = Guid.NewGuid(),
        MessageType = "Samples.OrderPlaced",
        MessageKind = kind,
        Payload = """{"orderId":42}""",
        CorrelationId = Guid.NewGuid(),
        CausationId = causationId,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // ── CreateMessage ──────────────────────────────────────────────────────────

    [Fact]
    public void CreateMessage_wraps_event_in_CloudEvents_structured_envelope()
    {
        var envelope = CreateEnvelope();
        var message = BuildMessage(MessageKind.Event);

        var sbMessage = envelope.CreateMessage(message);

        Assert.Equal(CloudEventsEnvelopeMapper.ContentType, sbMessage.ContentType);
        Assert.Equal(message.Id.ToString(), sbMessage.MessageId);
        Assert.Equal(message.CorrelationId.ToString(), sbMessage.CorrelationId);

        var cloudEvent = CloudEventsEnvelopeMapper.Deserialize(sbMessage.Body.ToString());
        Assert.Equal(message.MessageType, cloudEvent.Type);
        Assert.Equal(message.Id, cloudEvent.Id);
    }

    [Fact]
    public void CreateMessage_leaves_command_in_broker_native_format()
    {
        var envelope = CreateEnvelope();
        var message = BuildMessage(MessageKind.Command);

        var sbMessage = envelope.CreateMessage(message);

        Assert.Equal("application/json", sbMessage.ContentType);
        Assert.Equal(message.MessageType, sbMessage.ApplicationProperties["MessageType"]);
        Assert.Equal("Command", sbMessage.ApplicationProperties["MessageKind"]);
    }

    // ── Parse ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_reads_CloudEvents_structured_message()
    {
        var envelope = CreateEnvelope();
        var message = BuildMessage(MessageKind.Event);
        var json = CloudEventsEnvelopeMapper.Serialize(
            message, new CloudEventsOptions { Source = Source });

        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(json),
            contentType: CloudEventsEnvelopeMapper.ContentType);

        var parsed = envelope.Parse(received);

        Assert.Equal(message.MessageType, parsed.MessageType);
        Assert.Equal("Event", parsed.MessageKind);
        Assert.Equal(message.CorrelationId, parsed.CorrelationId);
        Assert.Equal(message.Id, parsed.MessageId);
    }

    [Fact]
    public void Parse_delegates_broker_native_message_to_inner_envelope()
    {
        var envelope = CreateEnvelope();
        var correlationId = Guid.NewGuid();

        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            contentType: "application/json",
            properties: new Dictionary<string, object>
            {
                ["MessageType"] = "Samples.Command",
                ["MessageKind"] = "Command",
                ["CorrelationId"] = correlationId.ToString(),
            });

        var parsed = envelope.Parse(received);

        Assert.Equal("Samples.Command", parsed.MessageType);
        Assert.Equal("Command", parsed.MessageKind);
        Assert.Equal(correlationId, parsed.CorrelationId);
    }

    [Fact]
    public void Parse_throws_MessageEnvelopeFormatException_for_malformed_CloudEvents_body()
    {
        var envelope = CreateEnvelope();
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("not json"),
            contentType: CloudEventsEnvelopeMapper.ContentType);

        var ex = Assert.Throws<MessageEnvelopeFormatException>(() => envelope.Parse(received));
        Assert.Equal("InvalidMessageFormat", ex.Reason);
    }

    [Fact]
    public void CreateMessage_uses_TypeFormatter_when_configured()
    {
        var envelope = CreateEnvelope(m => $"com.sierranl.{m.MessageType}");
        var message = BuildMessage(MessageKind.Event);

        var sbMessage = envelope.CreateMessage(message);
        var cloudEvent = CloudEventsEnvelopeMapper.Deserialize(sbMessage.Body.ToString());

        Assert.Equal($"com.sierranl.{message.MessageType}", cloudEvent.Type);
    }
}
