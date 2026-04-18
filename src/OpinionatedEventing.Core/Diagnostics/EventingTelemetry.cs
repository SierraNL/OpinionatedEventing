#nullable enable

namespace OpinionatedEventing.Diagnostics;

/// <summary>
/// Names of the <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/> used by OpinionatedEventing.
/// Consumers can pass these to OTel SDK registration helpers.
/// </summary>
public static class EventingTelemetry
{
    /// <summary>Name of the <see cref="System.Diagnostics.ActivitySource"/> emitted by this library.</summary>
    public const string ActivitySourceName = "OpinionatedEventing";

    /// <summary>Name of the <see cref="System.Diagnostics.Metrics.Meter"/> emitted by this library.</summary>
    public const string MeterName = "OpinionatedEventing";
}
