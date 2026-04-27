#nullable enable

using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpinionatedEventing.AzureServiceBus.HealthChecks;

// Placed in this namespace so the extension is available without an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IHealthChecksBuilder"/> for registering the Azure Service Bus connectivity health check.
/// </summary>
public static class AzureServiceBusHealthChecksBuilderExtensions
{
    /// <summary>
    /// Registers a liveness health check that verifies Azure Service Bus is reachable.
    /// Requires <c>ServiceBusAdministrationClient</c> to be registered (provided by <c>AddAzureServiceBusTransport</c>).
    /// </summary>
    /// <param name="builder">The health checks builder to extend.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static IHealthChecksBuilder AddAzureServiceBusConnectivityHealthCheck(
        this IHealthChecksBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddCheck<AzureServiceBusConnectivityHealthCheck>(
            "opinionatedeventing-azureservicebus",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["live", "broker"]);

        return builder;
    }
}
