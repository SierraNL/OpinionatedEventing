using System.Text.Json;

namespace OpinionatedEventing.Sagas.Options;

/// <summary>Options for the saga engine and timeout poller.</summary>
public sealed class SagaOptions
{
    /// <summary>
    /// How often <see cref="SagaTimeoutWorker"/> polls for expired saga instances.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan TimeoutCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// <see cref="JsonSerializerOptions"/> used to serialise and deserialise saga state payloads.
    /// When <see langword="null"/> (the default), falls back to
    /// <c>OpinionatedEventingOptions.SerializerOptions</c>; if that is also <see langword="null"/>,
    /// <see cref="JsonSerializerOptions.Default"/> is used.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }
}
