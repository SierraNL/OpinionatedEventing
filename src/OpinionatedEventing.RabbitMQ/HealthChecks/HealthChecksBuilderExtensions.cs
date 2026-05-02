#nullable enable

using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpinionatedEventing.RabbitMQ.HealthChecks;

// Placed in this namespace so the extension is available without an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IHealthChecksBuilder"/> for registering the RabbitMQ connectivity health check.
/// </summary>
public static class RabbitMqHealthChecksBuilderExtensions
{
    /// <summary>
    /// Registers a liveness health check that verifies the RabbitMQ broker connection is open.
    /// Requires <c>AddRabbitMQTransport()</c> to have been called so that the internal
    /// connection holder is available in the service collection.
    /// </summary>
    /// <param name="builder">The health checks builder to extend.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IHealthChecksBuilder AddRabbitMqConnectivityHealthCheck(
        this IHealthChecksBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddCheck<RabbitMqConnectivityHealthCheck>(
            "opinionatedeventing-rabbitmq",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["live", "broker"]);

        return builder;
    }
}
