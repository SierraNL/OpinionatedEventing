#nullable enable

using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace OpinionatedEventing.RabbitMQ.HealthChecks;

/// <summary>
/// Liveness health check that verifies the RabbitMQ broker connection is open.
/// Reports <see cref="HealthStatus.Unhealthy"/> if the <see cref="IConnection"/> is closed.
/// </summary>
internal sealed class RabbitMqConnectivityHealthCheck : IHealthCheck
{
    private readonly IConnection _connection;

    /// <summary>Initialises a new <see cref="RabbitMqConnectivityHealthCheck"/>.</summary>
    public RabbitMqConnectivityHealthCheck(IConnection connection)
    {
        _connection = connection;
    }

    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        HealthCheckResult result = _connection.IsOpen
            ? HealthCheckResult.Healthy("RabbitMQ connection is open.")
            : HealthCheckResult.Unhealthy("RabbitMQ connection is closed.");

        return Task.FromResult(result);
    }
}
