#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpinionatedEventing;
using OpinionatedEventing.HealthChecks;

// Placed in this namespace so the extension is available without an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IHealthChecksBuilder"/> for wiring up broker consumer pause behaviour.
/// </summary>
public static class OpinionatedEventingHealthChecksBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="HealthCheckConsumerPauseController"/> as both
    /// <see cref="IConsumerPauseController"/> and <see cref="IHealthCheckPublisher"/>,
    /// so that broker consumer workers automatically pause when a health check tagged
    /// <c>"pause"</c> becomes unhealthy, and resume when all such checks recover.
    /// </summary>
    /// <param name="builder">The health checks builder to extend.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Only checks explicitly tagged <c>"pause"</c> influence the pause decision.
    /// Tag your own dependency checks (e.g. database connectivity) with <c>"pause"</c>
    /// to use this feature.
    /// </para>
    /// <para>
    /// The default (always-consuming) behaviour is preserved when this method is not called.
    /// </para>
    /// </remarks>
    public static IHealthChecksBuilder WithConsumerPause(this IHealthChecksBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<HealthCheckConsumerPauseController>();

        // Remove any previously registered IConsumerPauseController (e.g. the NullConsumerPauseController
        // registered by the transport DI extensions) so there is exactly one implementation.
        var existing = builder.Services.FirstOrDefault(
            d => d.ServiceType == typeof(IConsumerPauseController));
        if (existing is not null)
            builder.Services.Remove(existing);

        builder.Services.AddSingleton<IConsumerPauseController>(
            sp => sp.GetRequiredService<HealthCheckConsumerPauseController>());
        builder.Services.AddSingleton<IHealthCheckPublisher>(
            sp => sp.GetRequiredService<HealthCheckConsumerPauseController>());

        return builder;
    }
}
