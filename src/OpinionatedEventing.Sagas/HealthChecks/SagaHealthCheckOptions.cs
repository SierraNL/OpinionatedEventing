#nullable enable

namespace OpinionatedEventing.Sagas.HealthChecks;

/// <summary>
/// Configures thresholds for <c>AddSagaTimeoutBacklogHealthCheck()</c>.
/// </summary>
public sealed class SagaHealthCheckOptions
{
    /// <summary>
    /// Gets or sets the expired-but-unprocessed saga count above which the saga timeout backlog check
    /// reports <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded"/>.
    /// Defaults to <c>10</c>.
    /// </summary>
    public int TimeoutBacklogThreshold { get; set; } = 10;
}
