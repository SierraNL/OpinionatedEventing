#nullable enable

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpinionatedEventing.Sagas.HealthChecks;

// Placed in this namespace so the extension is available without an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IHealthChecksBuilder"/> for registering the saga timeout backlog health check.
/// </summary>
public static class SagaHealthChecksBuilderExtensions
{
    /// <summary>
    /// Registers a readiness health check that reports <see cref="HealthStatus.Degraded"/> when the
    /// number of expired-but-unprocessed sagas exceeds the configured threshold.
    /// The check is skipped (reports <see cref="HealthStatus.Healthy"/>) if <c>ISagaStateStore</c>
    /// is not registered.
    /// </summary>
    /// <param name="builder">The health checks builder to extend.</param>
    /// <param name="configure">Optional delegate to configure <see cref="SagaHealthCheckOptions"/>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IHealthChecksBuilder AddSagaTimeoutBacklogHealthCheck(
        this IHealthChecksBuilder builder,
        Action<SagaHealthCheckOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configure is not null)
            builder.Services.Configure(configure);
        else
            builder.Services.AddOptions<SagaHealthCheckOptions>();

        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.AddCheck<SagaTimeoutBacklogHealthCheck>(
            "opinionatedeventing-saga-timeout-backlog",
            failureStatus: HealthStatus.Degraded,
            tags: ["ready", "saga"]);

        return builder;
    }
}
