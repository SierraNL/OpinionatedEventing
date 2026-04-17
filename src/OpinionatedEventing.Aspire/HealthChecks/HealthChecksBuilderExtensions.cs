#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpinionatedEventing.Aspire.HealthChecks;

// Placed in this namespace so the extension is available without an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IHealthChecksBuilder"/> for registering OpinionatedEventing health checks.
/// </summary>
public static class OpinionatedEventingHealthChecksBuilderExtensions
{
    /// <summary>
    /// Registers the following health checks:
    /// <list type="bullet">
    ///   <item><description>Broker connectivity (liveness) — checks RabbitMQ or Azure Service Bus.</description></item>
    ///   <item><description>Outbox backlog — <see cref="HealthStatus.Degraded"/> if pending message count exceeds the configured threshold.</description></item>
    ///   <item><description>Saga timeout backlog — <see cref="HealthStatus.Degraded"/> if expired-but-unprocessed saga count exceeds the configured threshold.</description></item>
    /// </list>
    /// </summary>
    /// <param name="builder">The health checks builder to extend.</param>
    /// <param name="configure">Optional delegate to configure <see cref="OpinionatedEventingHealthCheckOptions"/>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IHealthChecksBuilder AddOpinionatedEventingHealthChecks(
        this IHealthChecksBuilder builder,
        Action<OpinionatedEventingHealthCheckOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configure is not null)
            builder.Services.Configure(configure);
        else
            builder.Services.AddOptions<OpinionatedEventingHealthCheckOptions>();

        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.AddCheck<BrokerConnectivityHealthCheck>(
            "opinionatedeventing-broker",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["live", "broker"]);

        builder.AddCheck<OutboxBacklogHealthCheck>(
            "opinionatedeventing-outbox-backlog",
            failureStatus: HealthStatus.Degraded,
            tags: ["ready", "outbox"]);

        builder.AddCheck<SagaTimeoutBacklogHealthCheck>(
            "opinionatedeventing-saga-timeout-backlog",
            failureStatus: HealthStatus.Degraded,
            tags: ["ready", "saga"]);

        return builder;
    }
}
