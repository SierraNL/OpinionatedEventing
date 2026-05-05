#nullable enable

using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpinionatedEventing.Diagnostics;

namespace OpinionatedEventing.Sagas.Diagnostics;

internal static class SagaDiagnostics
{
    private static readonly ActivitySource _source = new(EventingTelemetry.ActivitySourceName, "1.0.0");

    // Static Meter and instruments live for the process lifetime; disposal is intentionally
    // omitted because the instruments must remain valid for background workers that outlive any
    // DI scope. This mirrors the standard pattern for library-owned meters in .NET.
    private static readonly Meter _meter = new(EventingTelemetry.MeterName, "1.0.0");

    internal static readonly UpDownCounter<long> Active =
        _meter.CreateUpDownCounter<long>(
            "opinionatedeventing.saga.active",
            description: "Number of currently active saga instances.");

    internal static readonly Counter<long> TimedOut =
        _meter.CreateCounter<long>(
            "opinionatedeventing.saga.timed_out",
            description: "Number of saga instances that reached their timeout.");

    internal static Activity? StartSagaStepActivity(string sagaType, string correlationKey, string eventType)
    {
        var activity = _source.StartActivity("saga.step", ActivityKind.Internal);
        if (activity is null) return null;

        activity.SetTag("saga.type", sagaType);
        activity.SetTag("saga.correlation_key", correlationKey);
        activity.SetTag("messaging.message.type", eventType);

        return activity;
    }

    internal static Activity? StartSagaTimeoutActivity(string sagaTypeName, string correlationId)
    {
        var activity = _source.StartActivity("saga.timeout", ActivityKind.Internal);
        if (activity is null) return null;

        activity.SetTag("saga.type", StripAssemblyInfo(sagaTypeName));
        activity.AddBaggage("messaging.message.correlation_id", correlationId);

        return activity;
    }

    // Strips ", AssemblyName, Version=..." from an AssemblyQualifiedName, leaving "Namespace.TypeName".
    private static string StripAssemblyInfo(string assemblyQualifiedName)
    {
        var comma = assemblyQualifiedName.IndexOf(',');
        return comma >= 0 ? assemblyQualifiedName[..comma] : assemblyQualifiedName;
    }
}
