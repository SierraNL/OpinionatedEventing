#nullable enable

using System.Globalization;
using System.Text;
using System.Text.Json;
using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.CloudEvents;

/// <summary>
/// Maps <see cref="OutboxMessage"/> to and from the CloudEvents 1.0 structured JSON envelope
/// (content mode <c>application/cloudevents+json</c>). Pure mapping logic with no broker
/// dependency — see <see cref="ContentType"/> for the content type to set on the outbound message.
/// </summary>
public static class CloudEventsEnvelopeMapper
{
    /// <summary>The content type of a CloudEvents structured envelope.</summary>
    public const string ContentType = "application/cloudevents+json";

    /// <summary>
    /// Serialises <paramref name="message"/> as a CloudEvents 1.0 structured JSON envelope.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="CloudEventsOptions.Source"/> is not configured.</exception>
    public static string Serialize(OutboxMessage message, CloudEventsOptions options)
    {
        if (options.Source is null)
            throw new InvalidOperationException(
                "CloudEventsOptions.Source must be configured, e.g. " +
                "UseCloudEventsEnvelope(opts => opts.Source = new Uri(\"urn:order-service\")).");

        var type = options.TypeFormatter?.Invoke(message) ?? message.MessageType;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("specversion", "1.0");
            writer.WriteString("type", type);
            writer.WriteString("source", options.Source.ToString());
            writer.WriteString("id", message.Id.ToString());
            writer.WriteString("time", message.CreatedAt);
            writer.WriteString("datacontenttype", "application/json");
            writer.WriteString("correlationid", message.CorrelationId.ToString());
            if (message.CausationId.HasValue)
                writer.WriteString("causationid", message.CausationId.Value.ToString());
            writer.WritePropertyName("data");
            writer.WriteRawValue(message.Payload, skipInputValidation: true);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Parses a CloudEvents 1.0 structured JSON envelope back into a <see cref="CloudEventEnvelope"/>.
    /// </summary>
    /// <exception cref="FormatException">The envelope is missing a required attribute or an attribute is malformed.</exception>
    public static CloudEventEnvelope Deserialize(string json)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new FormatException("CloudEvents envelope is not valid JSON.", ex);
        }

        // TryGetProperty throws InvalidOperationException (rather than returning false) when the
        // root isn't a JSON object, so a non-object envelope must be rejected explicitly here.
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("CloudEvents envelope must be a JSON object.");

        var idStr = GetRequiredString(root, "id");
        var type = GetRequiredString(root, "type");
        var sourceStr = GetRequiredString(root, "source");
        var timeStr = GetRequiredString(root, "time");
        var correlationIdStr = GetRequiredString(root, "correlationid");

        if (!root.TryGetProperty("data", out var dataElement))
            throw new FormatException("CloudEvents envelope is missing the required 'data' attribute.");

        if (!Guid.TryParse(idStr, out var id))
            throw new FormatException($"CloudEvents 'id' attribute '{idStr}' is not a valid Guid.");

        if (!Uri.TryCreate(sourceStr, UriKind.RelativeOrAbsolute, out var source))
            throw new FormatException($"CloudEvents 'source' attribute '{sourceStr}' is not a valid URI.");

        if (!DateTimeOffset.TryParse(
                timeStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time))
            throw new FormatException($"CloudEvents 'time' attribute '{timeStr}' is not a valid timestamp.");

        if (!Guid.TryParse(correlationIdStr, out var correlationId))
            throw new FormatException(
                $"CloudEvents 'correlationid' extension '{correlationIdStr}' is not a valid Guid.");

        Guid? causationId = null;
        if (root.TryGetProperty("causationid", out var causationElement)
            && causationElement.ValueKind == JsonValueKind.String)
        {
            var causationStr = causationElement.GetString();
            if (!Guid.TryParse(causationStr, out var parsedCausationId))
                throw new FormatException(
                    $"CloudEvents 'causationid' extension '{causationStr}' is not a valid Guid.");
            causationId = parsedCausationId;
        }

        return new CloudEventEnvelope(id, type, source, time, dataElement.GetRawText(), correlationId, causationId);
    }

    private static string GetRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
            throw new FormatException($"CloudEvents envelope is missing the required '{propertyName}' attribute.");
        // Safe: GetString() only returns null for JsonValueKind.Null, already excluded above.
        return element.GetString()!;
    }
}
