#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Sagas;

namespace OpinionatedEventing.Aspire.HealthChecks;

/// <summary>
/// Health check that reports <see cref="HealthStatus.Degraded"/> when the number of expired-but-unprocessed
/// sagas exceeds <see cref="OpinionatedEventingHealthCheckOptions.SagaTimeoutBacklogThreshold"/>.
/// Requires <see cref="ISagaStateStore"/> to be registered; skips the check if it is not.
/// </summary>
internal sealed class SagaTimeoutBacklogHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<OpinionatedEventingHealthCheckOptions> _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initialises a new <see cref="SagaTimeoutBacklogHealthCheck"/>.</summary>
    public SagaTimeoutBacklogHealthCheck(
        IServiceProvider serviceProvider,
        IOptions<OpinionatedEventingHealthCheckOptions> options,
        TimeProvider timeProvider)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var store = _serviceProvider.GetService<ISagaStateStore>();
        if (store is null)
            return HealthCheckResult.Healthy("ISagaStateStore not registered; saga timeout backlog check skipped.");

        var now = _timeProvider.GetUtcNow();
        var expired = await store.GetExpiredAsync(now, cancellationToken).ConfigureAwait(false);
        var count = expired.Count;
        var threshold = _options.Value.SagaTimeoutBacklogThreshold;

        return count > threshold
            ? HealthCheckResult.Degraded(
                $"Saga timeout backlog is {count} expired sagas, exceeding the threshold of {threshold}.")
            : HealthCheckResult.Healthy($"Saga timeout backlog is {count} expired sagas.");
    }
}
