#nullable enable

using OpenTelemetry.Metrics;
using OpinionatedEventing.Diagnostics;

namespace OpinionatedEventing.OpenTelemetry;

/// <summary>
/// Extension methods for registering OpinionatedEventing metrics with the OpenTelemetry SDK.
/// </summary>
public static class MeterProviderBuilderExtensions
{
    /// <summary>
    /// Enables metrics emitted by OpinionatedEventing (outbox pending/processed/failed, publish/dispatch/consume durations, saga active/timed-out).
    /// </summary>
    /// <param name="builder">The <see cref="MeterProviderBuilder"/> to configure.</param>
    /// <returns>The same <see cref="MeterProviderBuilder"/> for chaining.</returns>
    public static MeterProviderBuilder AddOpinionatedEventingMetrics(this MeterProviderBuilder builder)
        => builder.AddMeter(EventingTelemetry.MeterName);
}
