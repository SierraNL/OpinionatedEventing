#nullable enable

using System.Text;
using MSOptions = Microsoft.Extensions.Options.Options;
using OpinionatedEventing.CloudEvents;
using OpinionatedEventing.CloudEvents.RabbitMQ;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.RabbitMQ;
using RabbitMQ.Client;
using Xunit;

namespace OpinionatedEventing.CloudEvents.RabbitMQ.Tests;

public sealed class CloudEventsRabbitMQMessageEnvelopeTests
{
    private static readonly Uri Source = new("urn:order-service");

    private static CloudEventsRabbitMQMessageEnvelope CreateEnvelope(Func<OutboxMessage, string>? typeFormatter = null)
        => new(new DefaultRabbitMQMessageEnvelope(),
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

        var result = envelope.CreateMessage(message);

        Assert.Equal(CloudEventsEnvelopeMapper.ContentType, result.Properties.ContentType);
        Assert.Equal(message.Id.ToString(), result.Properties.MessageId);
        Assert.NotNull(result.Properties.Headers);

        var cloudEvent = CloudEventsEnvelopeMapper.Deserialize(Encoding.UTF8.GetString(result.Body.Span));
        Assert.Equal(message.MessageType, cloudEvent.Type);
        Assert.Equal(message.Id, cloudEvent.Id);
    }

    [Fact]
    public void CreateMessage_leaves_command_in_broker_native_format()
    {
        var envelope = CreateEnvelope();
        var message = BuildMessage(MessageKind.Command);

        var result = envelope.CreateMessage(message);

        Assert.Equal("application/json", result.Properties.ContentType);
        Assert.Equal(message.MessageType, result.Properties.Headers!["MessageType"]);
        Assert.Equal("Command", result.Properties.Headers!["MessageKind"]);
    }

    [Fact]
    public void CreateMessage_uses_TypeFormatter_when_configured()
    {
        var envelope = CreateEnvelope(m => $"com.sierranl.{m.MessageType}");
        var message = BuildMessage(MessageKind.Event);

        var result = envelope.CreateMessage(message);
        var cloudEvent = CloudEventsEnvelopeMapper.Deserialize(Encoding.UTF8.GetString(result.Body.Span));

        Assert.Equal($"com.sierranl.{message.MessageType}", cloudEvent.Type);
    }

    // ── Parse ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_reads_CloudEvents_structured_message()
    {
        var envelope = CreateEnvelope();
        var message = BuildMessage(MessageKind.Event);
        var json = CloudEventsEnvelopeMapper.Serialize(
            message, new CloudEventsOptions { Source = Source });

        var properties = new BasicProperties { ContentType = CloudEventsEnvelopeMapper.ContentType };
        var body = Encoding.UTF8.GetBytes(json);

        var parsed = envelope.Parse(properties, body);

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

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            Headers = new Dictionary<string, object?>
            {
                ["MessageType"] = "Samples.Command",
                ["MessageKind"] = "Command",
                ["CorrelationId"] = correlationId.ToString(),
            },
        };

        var parsed = envelope.Parse(properties, Encoding.UTF8.GetBytes("{}"));

        Assert.Equal("Samples.Command", parsed.MessageType);
        Assert.Equal("Command", parsed.MessageKind);
        Assert.Equal(correlationId, parsed.CorrelationId);
    }

    [Fact]
    public void Parse_throws_MessageEnvelopeFormatException_for_malformed_CloudEvents_body()
    {
        var envelope = CreateEnvelope();
        var properties = new BasicProperties { ContentType = CloudEventsEnvelopeMapper.ContentType };

        var ex = Assert.Throws<MessageEnvelopeFormatException>(
            () => envelope.Parse(properties, Encoding.UTF8.GetBytes("not json")));
        Assert.Equal("InvalidMessageFormat", ex.Reason);
    }
}
