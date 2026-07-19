#nullable enable

using System.Text;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.RabbitMQ;
using RabbitMQ.Client;
using Xunit;

namespace OpinionatedEventing.RabbitMQ.Tests;

public sealed class DefaultRabbitMQMessageEnvelopeTests
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
        var envelope = new DefaultRabbitMQMessageEnvelope();
        var message = BuildMessage();

        var result = envelope.CreateMessage(message);

        Assert.Equal(message.Payload, Encoding.UTF8.GetString(result.Body.Span));
        Assert.Equal(message.Id.ToString(), result.Properties.MessageId);
        Assert.Equal("application/json", result.Properties.ContentType);
        Assert.Equal(message.CorrelationId.ToString(), result.Properties.CorrelationId);
        Assert.Equal(DeliveryModes.Persistent, result.Properties.DeliveryMode);
        Assert.Equal(message.MessageType, result.Properties.Headers!["MessageType"]);
        Assert.Equal("Event", result.Properties.Headers!["MessageKind"]);
        Assert.Equal(message.CorrelationId.ToString(), result.Properties.Headers!["CorrelationId"]);
        Assert.False(result.Properties.Headers!.ContainsKey("CausationId"));
    }

    [Fact]
    public void CreateMessage_includes_CausationId_when_set()
    {
        var envelope = new DefaultRabbitMQMessageEnvelope();
        var causationId = Guid.NewGuid();
        var message = BuildMessage(causationId);

        var result = envelope.CreateMessage(message);

        Assert.Equal(causationId.ToString(), result.Properties.Headers!["CausationId"]);
    }

    // ── Parse ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_returns_envelope_with_MessageId_as_CausationId()
    {
        var envelope = new DefaultRabbitMQMessageEnvelope();
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var properties = new BasicProperties
        {
            MessageId = messageId.ToString(),
            Headers = new Dictionary<string, object?>
            {
                ["MessageType"] = "Samples.OrderPlaced",
                ["MessageKind"] = "Event",
                ["CorrelationId"] = correlationId.ToString(),
            },
        };

        var parsed = envelope.Parse(properties, Encoding.UTF8.GetBytes("{}"));

        Assert.Equal("Samples.OrderPlaced", parsed.MessageType);
        Assert.Equal("Event", parsed.MessageKind);
        Assert.Equal(correlationId, parsed.CorrelationId);
        Assert.Equal(messageId, parsed.MessageId);
        Assert.Equal(messageId, parsed.CausationId);
        Assert.Equal("{}", parsed.Payload);
    }

    [Fact]
    public void Parse_reads_header_values_encoded_as_byte_arrays()
    {
        // RabbitMQ.Client decodes headers received over the wire as byte[] rather than string.
        var envelope = new DefaultRabbitMQMessageEnvelope();
        var correlationId = Guid.NewGuid();

        var properties = new BasicProperties
        {
            Headers = new Dictionary<string, object?>
            {
                ["MessageType"] = Encoding.UTF8.GetBytes("Samples.OrderPlaced"),
                ["MessageKind"] = Encoding.UTF8.GetBytes("Event"),
                ["CorrelationId"] = Encoding.UTF8.GetBytes(correlationId.ToString()),
            },
        };

        var parsed = envelope.Parse(properties, Encoding.UTF8.GetBytes("{}"));

        Assert.Equal("Samples.OrderPlaced", parsed.MessageType);
        Assert.Equal("Event", parsed.MessageKind);
        Assert.Equal(correlationId, parsed.CorrelationId);
    }

    [Fact]
    public void Parse_returns_null_MessageId_and_CausationId_when_MessageId_is_not_a_guid()
    {
        var envelope = new DefaultRabbitMQMessageEnvelope();
        var properties = new BasicProperties
        {
            MessageId = "not-a-guid",
            Headers = new Dictionary<string, object?>
            {
                ["MessageType"] = "Samples.OrderPlaced",
                ["MessageKind"] = "Event",
                ["CorrelationId"] = Guid.NewGuid().ToString(),
            },
        };

        var parsed = envelope.Parse(properties, Encoding.UTF8.GetBytes("{}"));

        Assert.Null(parsed.MessageId);
        Assert.Null(parsed.CausationId);
    }

    [Fact]
    public void Parse_throws_when_Headers_is_null()
    {
        var envelope = new DefaultRabbitMQMessageEnvelope();
        var properties = new BasicProperties();

        var ex = Assert.Throws<MessageEnvelopeFormatException>(
            () => envelope.Parse(properties, Encoding.UTF8.GetBytes("{}")));
        Assert.Equal("InvalidMessageFormat", ex.Reason);
    }

    [Theory]
    [InlineData("MessageType")]
    [InlineData("MessageKind")]
    [InlineData("CorrelationId")]
    public void Parse_throws_when_a_required_header_is_missing(string missingHeader)
    {
        var envelope = new DefaultRabbitMQMessageEnvelope();
        var headers = new Dictionary<string, object?>
        {
            ["MessageType"] = "Samples.OrderPlaced",
            ["MessageKind"] = "Event",
            ["CorrelationId"] = Guid.NewGuid().ToString(),
        };
        headers.Remove(missingHeader);
        var properties = new BasicProperties { Headers = headers };

        var ex = Assert.Throws<MessageEnvelopeFormatException>(
            () => envelope.Parse(properties, Encoding.UTF8.GetBytes("{}")));
        Assert.Equal("InvalidMessageFormat", ex.Reason);
    }

    [Fact]
    public void Parse_throws_when_CorrelationId_is_not_a_valid_guid()
    {
        var envelope = new DefaultRabbitMQMessageEnvelope();
        var properties = new BasicProperties
        {
            Headers = new Dictionary<string, object?>
            {
                ["MessageType"] = "Samples.OrderPlaced",
                ["MessageKind"] = "Event",
                ["CorrelationId"] = "not-a-guid",
            },
        };

        var ex = Assert.Throws<MessageEnvelopeFormatException>(
            () => envelope.Parse(properties, Encoding.UTF8.GetBytes("{}")));
        Assert.Equal("InvalidMessageFormat", ex.Reason);
        Assert.Equal("CorrelationId is not a valid Guid.", ex.Description);
    }
}
