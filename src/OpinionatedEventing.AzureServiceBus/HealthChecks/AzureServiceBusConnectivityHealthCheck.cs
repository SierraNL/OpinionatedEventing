#nullable enable

using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OpinionatedEventing.AzureServiceBus.HealthChecks;

/// <summary>
/// Liveness health check that verifies Azure Service Bus is reachable by calling
/// <see cref="ServiceBusAdministrationClient.GetNamespacePropertiesAsync"/>.
/// Reports <see cref="HealthStatus.Unhealthy"/> if the call fails.
/// </summary>
internal sealed class AzureServiceBusConnectivityHealthCheck : IHealthCheck
{
    private readonly ServiceBusAdministrationClient _adminClient;

    /// <summary>Initialises a new <see cref="AzureServiceBusConnectivityHealthCheck"/>.</summary>
    public AzureServiceBusConnectivityHealthCheck(ServiceBusAdministrationClient adminClient)
    {
        _adminClient = adminClient;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _adminClient.GetNamespacePropertiesAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("Azure Service Bus is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Azure Service Bus connectivity check failed.", ex);
        }
    }
}
