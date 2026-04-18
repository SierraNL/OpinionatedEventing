#nullable enable

using OpenTelemetry.Trace;
using OpinionatedEventing.Diagnostics;

namespace OpinionatedEventing.OpenTelemetry;

/// <summary>
/// Extension methods for registering OpinionatedEventing distributed tracing with the OpenTelemetry SDK.
/// </summary>
public static class TracerProviderBuilderExtensions
{
    /// <summary>
    /// Enables distributed tracing spans emitted by OpinionatedEventing (outbox write, dispatch, consume, saga steps).
    /// </summary>
    /// <param name="builder">The <see cref="TracerProviderBuilder"/> to configure.</param>
    /// <returns>The same <see cref="TracerProviderBuilder"/> for chaining.</returns>
    public static TracerProviderBuilder AddOpinionatedEventingInstrumentation(this TracerProviderBuilder builder)
        => builder.AddSource(EventingTelemetry.ActivitySourceName);
}
