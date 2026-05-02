#nullable enable

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OpinionatedEventing.RabbitMQ.HealthChecks;

/// <summary>
/// Liveness health check that verifies the RabbitMQ broker connection is open.
/// Reports <see cref="HealthStatus.Unhealthy"/> if the connection is not yet established or
/// is closed.
/// </summary>
internal sealed class RabbitMqConnectivityHealthCheck : IHealthCheck
{
    private readonly RabbitMqConnectionHolder _holder;

    /// <summary>Initialises a new <see cref="RabbitMqConnectivityHealthCheck"/>.</summary>
    public RabbitMqConnectivityHealthCheck(RabbitMqConnectionHolder holder)
    {
        _holder = holder;
    }

    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connection = _holder.TryGetConnection();

        HealthCheckResult result = connection?.IsOpen == true
            ? HealthCheckResult.Healthy("RabbitMQ connection is open.")
            : HealthCheckResult.Unhealthy("RabbitMQ connection is closed.");

        return Task.FromResult(result);
    }
}
