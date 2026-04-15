using System.Text.Json;

namespace OpinionatedEventing.Options;

/// <summary>
/// Top-level configuration options for OpinionatedEventing.
/// Pass an <see cref="Action{T}"/> to <c>AddOpinionatedEventing</c> to configure these values.
/// </summary>
public sealed class OpinionatedEventingOptions
{
    /// <summary>Gets the outbox dispatcher options.</summary>
    public OutboxOptions Outbox { get; } = new();

    /// <summary>
    /// Gets or sets the <see cref="JsonSerializerOptions"/> used to serialise and deserialise
    /// message payloads. Defaults to <see langword="null"/>, which uses the
    /// <see cref="JsonSerializerOptions.Default"/> options.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }
}
