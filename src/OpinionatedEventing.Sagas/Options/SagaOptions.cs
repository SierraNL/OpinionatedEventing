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
    /// Defaults to <see langword="null"/>, which uses <see cref="JsonSerializerOptions.Default"/>.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }
}
