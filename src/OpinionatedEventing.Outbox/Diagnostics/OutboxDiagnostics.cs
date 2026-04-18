#nullable enable

using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpinionatedEventing.Diagnostics;

namespace OpinionatedEventing.Outbox.Diagnostics;

internal static class OutboxDiagnostics
{
    private static readonly ActivitySource _source = new(EventingTelemetry.ActivitySourceName, "1.0.0");

    // Static Meter and instruments live for the process lifetime; disposal is intentionally
    // omitted because the instruments must remain valid for background workers that outlive any
    // DI scope. This mirrors the standard pattern for library-owned meters in .NET.
    private static readonly Meter _meter = new(EventingTelemetry.MeterName, "1.0.0");

    internal static readonly UpDownCounter<long> Pending =
        _meter.CreateUpDownCounter<long>(
            "opinionatedeventing.outbox.pending",
            description: "Number of messages currently pending in the outbox.");

    internal static readonly Counter<long> Processed =
        _meter.CreateCounter<long>(
            "opinionatedeventing.outbox.processed",
            description: "Number of outbox messages successfully dispatched to the broker.");

    internal static readonly Counter<long> Failed =
        _meter.CreateCounter<long>(
            "opinionatedeventing.outbox.failed",
            description: "Number of outbox messages dead-lettered after exhausting retry attempts.");

    internal static readonly Histogram<double> PublishDuration =
        _meter.CreateHistogram<double>(
            "opinionatedeventing.publish.duration",
            unit: "ms",
            description: "Duration of outbox write operations (PublishEventAsync / SendCommandAsync).");

    internal static readonly Histogram<double> DispatchDuration =
        _meter.CreateHistogram<double>(
            "opinionatedeventing.dispatch.duration",
            unit: "ms",
            description: "Duration of outbox dispatch operations (poll to broker send).");

    internal static Activity? StartPublishActivity(string messageType, string messageKind, Guid correlationId, Guid? causationId)
    {
        var activity = _source.StartActivity("outbox.write", ActivityKind.Producer);
        if (activity is null) return null;

        activity.SetTag("messaging.operation", "publish");
        activity.SetTag("messaging.message.type", StripAssemblyInfo(messageType));
        activity.SetTag("messaging.message.kind", messageKind);
        activity.AddBaggage("correlation.id", correlationId.ToString());
        if (causationId.HasValue)
            activity.AddBaggage("causation.id", causationId.Value.ToString());

        return activity;
    }

    internal static Activity? StartDispatchActivity(Guid messageId, string messageType)
    {
        var activity = _source.StartActivity("outbox.dispatch", ActivityKind.Producer);
        if (activity is null) return null;

        activity.SetTag("messaging.operation", "dispatch");
        activity.SetTag("messaging.message.id", messageId.ToString());
        activity.SetTag("messaging.message.type", StripAssemblyInfo(messageType));

        return activity;
    }

    // Strips ", AssemblyName, Version=..." from an AssemblyQualifiedName, leaving "Namespace.TypeName".
    private static string StripAssemblyInfo(string assemblyQualifiedName)
    {
        var comma = assemblyQualifiedName.IndexOf(',');
        return comma >= 0 ? assemblyQualifiedName[..comma] : assemblyQualifiedName;
    }
}
