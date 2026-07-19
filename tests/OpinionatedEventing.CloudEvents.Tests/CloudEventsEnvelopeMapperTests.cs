#nullable enable

using System.Text.Json;
using OpinionatedEventing.CloudEvents;
using OpinionatedEventing.Outbox;
using Xunit;

namespace OpinionatedEventing.CloudEvents.Tests;

public sealed class CloudEventsEnvelopeMapperTests
{
    private static OutboxMessage BuildMessage(Guid? causationId = null) => new()
    {
        Id = Guid.NewGuid(),
        MessageType = "Samples.OrderPlaced",
        MessageKind = MessageKind.Event,
        Payload = """{"orderId":42,"total":9.99}""",
        CorrelationId = Guid.NewGuid(),
        CausationId = causationId,
        CreatedAt = DateTimeOffset.Parse("2026-01-15T10:30:00Z"),
    };

    // ── Serialize ──────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_writes_all_required_attributes()
    {
        var message = BuildMessage();
        var options = new CloudEventsOptions { Source = new Uri("urn:order-service") };

        var json = CloudEventsEnvelopeMapper.Serialize(message, options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("1.0", root.GetProperty("specversion").GetString());
        Assert.Equal(message.MessageType, root.GetProperty("type").GetString());
        Assert.Equal("urn:order-service", root.GetProperty("source").GetString());
        Assert.Equal(message.Id.ToString(), root.GetProperty("id").GetString());
        Assert.Equal("application/json", root.GetProperty("datacontenttype").GetString());
        Assert.Equal(message.CorrelationId.ToString(), root.GetProperty("correlationid").GetString());
        Assert.Equal(message.CreatedAt, DateTimeOffset.Parse(root.GetProperty("time").GetString()!));
        Assert.Equal(42, root.GetProperty("data").GetProperty("orderId").GetInt32());
    }

    [Fact]
    public void Serialize_omits_causationid_when_null()
    {
        var message = BuildMessage(causationId: null);
        var options = new CloudEventsOptions { Source = new Uri("urn:order-service") };

        var json = CloudEventsEnvelopeMapper.Serialize(message, options);
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.TryGetProperty("causationid", out _));
    }

    [Fact]
    public void Serialize_includes_causationid_when_set()
    {
        var causationId = Guid.NewGuid();
        var message = BuildMessage(causationId);
        var options = new CloudEventsOptions { Source = new Uri("urn:order-service") };

        var json = CloudEventsEnvelopeMapper.Serialize(message, options);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(causationId.ToString(), doc.RootElement.GetProperty("causationid").GetString());
    }

    [Fact]
    public void Serialize_uses_TypeFormatter_when_configured()
    {
        var message = BuildMessage();
        var options = new CloudEventsOptions
        {
            Source = new Uri("urn:order-service"),
            TypeFormatter = m => $"com.sierranl.orders.{m.MessageType}",
        };

        var json = CloudEventsEnvelopeMapper.Serialize(message, options);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("com.sierranl.orders.Samples.OrderPlaced", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public void Serialize_throws_when_Source_is_not_configured()
    {
        var message = BuildMessage();
        var options = new CloudEventsOptions();

        Assert.Throws<InvalidOperationException>(() => CloudEventsEnvelopeMapper.Serialize(message, options));
    }

    // ── Deserialize ────────────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_roundtrips_Serialize_output()
    {
        var causationId = Guid.NewGuid();
        var message = BuildMessage(causationId);
        var options = new CloudEventsOptions { Source = new Uri("urn:order-service") };
        var json = CloudEventsEnvelopeMapper.Serialize(message, options);

        var cloudEvent = CloudEventsEnvelopeMapper.Deserialize(json);

        Assert.Equal(message.Id, cloudEvent.Id);
        Assert.Equal(message.MessageType, cloudEvent.Type);
        Assert.Equal(options.Source, cloudEvent.Source);
        Assert.Equal(message.CreatedAt, cloudEvent.Time);
        Assert.Equal(message.CorrelationId, cloudEvent.CorrelationId);
        Assert.Equal(causationId, cloudEvent.CausationId);
        Assert.Equal(JsonDocument.Parse(message.Payload).RootElement.ToString(),
            JsonDocument.Parse(cloudEvent.Data).RootElement.ToString());
    }

    [Fact]
    public void Deserialize_returns_null_CausationId_when_absent()
    {
        var message = BuildMessage(causationId: null);
        var options = new CloudEventsOptions { Source = new Uri("urn:order-service") };
        var json = CloudEventsEnvelopeMapper.Serialize(message, options);

        var cloudEvent = CloudEventsEnvelopeMapper.Deserialize(json);

        Assert.Null(cloudEvent.CausationId);
    }

    [Fact]
    public void Deserialize_throws_FormatException_for_invalid_json()
    {
        Assert.Throws<FormatException>(() => CloudEventsEnvelopeMapper.Deserialize("not json"));
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("42")]
    [InlineData("\"a string\"")]
    [InlineData("null")]
    public void Deserialize_throws_FormatException_when_root_is_not_a_json_object(string json)
    {
        Assert.Throws<FormatException>(() => CloudEventsEnvelopeMapper.Deserialize(json));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("type")]
    [InlineData("source")]
    [InlineData("time")]
    [InlineData("correlationid")]
    [InlineData("data")]
    public void Deserialize_throws_FormatException_when_required_attribute_missing(string missingProperty)
    {
        var message = BuildMessage();
        var options = new CloudEventsOptions { Source = new Uri("urn:order-service") };
        var json = CloudEventsEnvelopeMapper.Serialize(message, options);

        using var doc = JsonDocument.Parse(json);
        var jsonObject = new Dictionary<string, JsonElement>();
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Name != missingProperty)
                jsonObject[property.Name] = property.Value.Clone();
        }
        var mutatedJson = JsonSerializer.Serialize(jsonObject);

        Assert.Throws<FormatException>(() => CloudEventsEnvelopeMapper.Deserialize(mutatedJson));
    }

    [Fact]
    public void Deserialize_throws_FormatException_when_id_is_not_a_guid()
    {
        var json = """{"specversion":"1.0","type":"t","source":"urn:s","id":"not-a-guid","time":"2026-01-01T00:00:00Z","datacontenttype":"application/json","correlationid":"11111111-1111-1111-1111-111111111111","data":{}}""";

        Assert.Throws<FormatException>(() => CloudEventsEnvelopeMapper.Deserialize(json));
    }

    [Fact]
    public void Deserialize_throws_FormatException_when_causationid_is_not_a_guid()
    {
        var json = """{"specversion":"1.0","type":"t","source":"urn:s","id":"11111111-1111-1111-1111-111111111111","time":"2026-01-01T00:00:00Z","datacontenttype":"application/json","correlationid":"11111111-1111-1111-1111-111111111111","causationid":"not-a-guid","data":{}}""";

        Assert.Throws<FormatException>(() => CloudEventsEnvelopeMapper.Deserialize(json));
    }
}
