#nullable enable

using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace OpinionatedEventing.Aspire.HealthChecks;

/// <summary>
/// Liveness health check that verifies broker connectivity.
/// Supports both RabbitMQ (<see cref="IConnection"/>) and Azure Service Bus
/// (<see cref="ServiceBusAdministrationClient"/>). If neither is registered, reports healthy.
/// </summary>
internal sealed class BrokerConnectivityHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>Initialises a new <see cref="BrokerConnectivityHealthCheck"/>.</summary>
    public BrokerConnectivityHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var rabbitConnection = _serviceProvider.GetService<IConnection>();
        if (rabbitConnection is not null)
        {
            return rabbitConnection.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ connection is open.")
                : HealthCheckResult.Unhealthy("RabbitMQ connection is closed.");
        }

        var asbAdminClient = _serviceProvider.GetService<ServiceBusAdministrationClient>();
        if (asbAdminClient is not null)
        {
            try
            {
                await asbAdminClient.GetNamespacePropertiesAsync(cancellationToken).ConfigureAwait(false);
                return HealthCheckResult.Healthy("Azure Service Bus is reachable.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Azure Service Bus connectivity check failed.", ex);
            }
        }

        return HealthCheckResult.Healthy("No broker transport registered.");
    }
}
