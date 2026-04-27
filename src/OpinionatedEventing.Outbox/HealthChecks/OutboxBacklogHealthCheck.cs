#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace OpinionatedEventing.Outbox.HealthChecks;

/// <summary>
/// Health check that reports <see cref="HealthStatus.Degraded"/> when the number of pending
/// outbox messages exceeds <see cref="OutboxHealthCheckOptions.BacklogThreshold"/>.
/// Requires <see cref="IOutboxMonitor"/> to be registered; skips the check if it is not.
/// </summary>
internal sealed class OutboxBacklogHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<OutboxHealthCheckOptions> _options;

    /// <summary>Initialises a new <see cref="OutboxBacklogHealthCheck"/>.</summary>
    public OutboxBacklogHealthCheck(
        IServiceProvider serviceProvider,
        IOptions<OutboxHealthCheckOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var monitor = _serviceProvider.GetService<IOutboxMonitor>();
        if (monitor is null)
            return HealthCheckResult.Healthy("IOutboxMonitor not registered; outbox backlog check skipped.");

        var pending = await monitor.GetPendingCountAsync(cancellationToken).ConfigureAwait(false);
        var threshold = _options.Value.BacklogThreshold;

        return pending > threshold
            ? HealthCheckResult.Degraded(
                $"Outbox backlog is {pending} messages, exceeding the threshold of {threshold}.")
            : HealthCheckResult.Healthy($"Outbox backlog is {pending} messages.");
    }
}
