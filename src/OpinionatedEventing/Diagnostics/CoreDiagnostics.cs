#nullable enable

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OpinionatedEventing.Diagnostics;

internal static class CoreDiagnostics
{
    private static readonly ActivitySource _source = new(EventingTelemetry.ActivitySourceName, "1.0.0");

    // Static Meter and instruments live for the process lifetime; disposal is intentionally
    // omitted because the instruments must remain valid for background workers that outlive any
    // DI scope. This mirrors the standard pattern for library-owned meters in .NET.
    private static readonly Meter _meter = new(EventingTelemetry.MeterName, "1.0.0");

    internal static readonly Histogram<double> ConsumeDuration =
        _meter.CreateHistogram<double>(
            "opinionatedeventing.consume.duration",
            unit: "ms",
            description: "Duration of message handler execution from broker receive to handler complete.");

    internal static Activity? StartConsumeActivity(string messageType, string messageKind, Guid correlationId, Guid? causationId)
    {
        var activity = _source.StartActivity("consume", ActivityKind.Consumer);
        if (activity is null) return null;

        activity.SetTag("messaging.operation", "receive");
        activity.SetTag("messaging.message.type", messageType);
        activity.SetTag("messaging.message.kind", messageKind);
        activity.AddBaggage("messaging.message.correlation_id", correlationId.ToString());
        if (causationId.HasValue)
            activity.AddBaggage("messaging.message.causation_id", causationId.Value.ToString());

        return activity;
    }
}
