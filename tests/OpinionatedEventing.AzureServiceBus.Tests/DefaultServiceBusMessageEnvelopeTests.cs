#nullable enable

using Azure.Messaging.ServiceBus;
using OpinionatedEventing.AzureServiceBus;
using OpinionatedEventing.Outbox;
using Xunit;

namespace OpinionatedEventing.AzureServiceBus.Tests;

public sealed class DefaultServiceBusMessageEnvelopeTests
{
    private static OutboxMessage BuildMessage(Guid? causationId = null) => new()
    {
        Id = Guid.NewGuid(),
        MessageType = "Samples.OrderPlaced",
        MessageKind = MessageKind.Event,
        Payload = """{"orderId":42}""",
        CorrelationId = Guid.NewGuid(),
        CausationId = causationId,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // ── CreateMessage ──────────────────────────────────────────────────────────

    [Fact]
    public void CreateMessage_sets_body_and_broker_native_properties()
    {
        var envelope = new DefaultServiceBusMessageEnvelope();
        var message = BuildMessage();

        var sbMessage = envelope.CreateMessage(message);

        Assert.Equal(message.Payload, sbMessage.Body.ToString());
        Assert.Equal(message.Id.ToString(), sbMessage.MessageId);
        Assert.Equal("application/json", sbMessage.ContentType);
        Assert.Equal(message.CorrelationId.ToString(), sbMessage.CorrelationId);
        Assert.Equal(message.MessageType, sbMessage.ApplicationProperties["MessageType"]);
        Assert.Equal("Event", sbMessage.ApplicationProperties["MessageKind"]);
        Assert.Equal(message.CorrelationId.ToString(), sbMessage.ApplicationProperties["CorrelationId"]);
        Assert.False(sbMessage.ApplicationProperties.ContainsKey("CausationId"));
    }

    [Fact]
    public void CreateMessage_includes_CausationId_when_set()
    {
        var envelope = new DefaultServiceBusMessageEnvelope();
        var causationId = Guid.NewGuid();
        var message = BuildMessage(causationId);

        var sbMessage = envelope.CreateMessage(message);

        Assert.Equal(causationId.ToString(), sbMessage.ApplicationProperties["CausationId"]);
    }

    // ── Parse ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_returns_envelope_with_MessageId_as_CausationId()
    {
        var envelope = new DefaultServiceBusMessageEnvelope();
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: messageId.ToString(),
            properties: new Dictionary<string, object>
            {
                ["MessageType"] = "Samples.OrderPlaced",
                ["MessageKind"] = "Event",
                ["CorrelationId"] = correlationId.ToString(),
            });

        var parsed = envelope.Parse(received);

        Assert.Equal("Samples.OrderPlaced", parsed.MessageType);
        Assert.Equal("Event", parsed.MessageKind);
        Assert.Equal(correlationId, parsed.CorrelationId);
        Assert.Equal(messageId, parsed.MessageId);
        Assert.Equal(messageId, parsed.CausationId);
        Assert.Equal("{}", parsed.Payload);
    }

    [Fact]
    public void Parse_returns_null_MessageId_and_CausationId_when_MessageId_is_not_a_guid()
    {
        var envelope = new DefaultServiceBusMessageEnvelope();
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: "not-a-guid",
            properties: new Dictionary<string, object>
            {
                ["MessageType"] = "Samples.OrderPlaced",
                ["MessageKind"] = "Event",
                ["CorrelationId"] = Guid.NewGuid().ToString(),
            });

        var parsed = envelope.Parse(received);

        Assert.Null(parsed.MessageId);
        Assert.Null(parsed.CausationId);
    }

    [Theory]
    [InlineData("MessageType")]
    [InlineData("MessageKind")]
    [InlineData("CorrelationId")]
    public void Parse_throws_when_a_required_property_is_missing(string missingProperty)
    {
        var envelope = new DefaultServiceBusMessageEnvelope();
        var properties = new Dictionary<string, object>
        {
            ["MessageType"] = "Samples.OrderPlaced",
            ["MessageKind"] = "Event",
            ["CorrelationId"] = Guid.NewGuid().ToString(),
        };
        properties.Remove(missingProperty);

        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"), properties: properties);

        var ex = Assert.Throws<MessageEnvelopeFormatException>(() => envelope.Parse(received));
        Assert.Equal("InvalidMessageFormat", ex.Reason);
    }

    [Fact]
    public void Parse_throws_when_CorrelationId_is_not_a_valid_guid()
    {
        var envelope = new DefaultServiceBusMessageEnvelope();
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            properties: new Dictionary<string, object>
            {
                ["MessageType"] = "Samples.OrderPlaced",
                ["MessageKind"] = "Event",
                ["CorrelationId"] = "not-a-guid",
            });

        var ex = Assert.Throws<MessageEnvelopeFormatException>(() => envelope.Parse(received));
        Assert.Equal("InvalidMessageFormat", ex.Reason);
        Assert.Equal("CorrelationId is not a valid Guid.", ex.Description);
    }
}
