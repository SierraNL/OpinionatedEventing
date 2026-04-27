#nullable enable

using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpinionatedEventing.Outbox.HealthChecks;

// Placed in this namespace so the extension is available without an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IHealthChecksBuilder"/> for registering the outbox backlog health check.
/// </summary>
public static class OutboxHealthChecksBuilderExtensions
{
    /// <summary>
    /// Registers a readiness health check that reports <see cref="HealthStatus.Degraded"/> when the
    /// number of pending outbox messages exceeds the configured threshold.
    /// The check is skipped (reports <see cref="HealthStatus.Healthy"/>) if <c>IOutboxMonitor</c>
    /// is not registered.
    /// </summary>
    /// <param name="builder">The health checks builder to extend.</param>
    /// <param name="configure">Optional delegate to configure <see cref="OutboxHealthCheckOptions"/>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IHealthChecksBuilder AddOutboxBacklogHealthCheck(
        this IHealthChecksBuilder builder,
        Action<OutboxHealthCheckOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configure is not null)
            builder.Services.Configure(configure);
        else
            builder.Services.AddOptions<OutboxHealthCheckOptions>();

        builder.AddCheck<OutboxBacklogHealthCheck>(
            "opinionatedeventing-outbox-backlog",
            failureStatus: HealthStatus.Degraded,
            tags: ["ready", "outbox"]);

        return builder;
    }
}
