#nullable enable

namespace OpinionatedEventing.Aspire.HealthChecks;

/// <summary>
/// Configures thresholds for <c>AddOpinionatedEventingHealthChecks()</c>.
/// </summary>
public sealed class OpinionatedEventingHealthCheckOptions
{
    /// <summary>
    /// Gets or sets the outbox pending-message count above which the outbox backlog check reports
    /// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded"/>.
    /// Defaults to <c>100</c>.
    /// </summary>
    public int OutboxBacklogThreshold { get; set; } = 100;

    /// <summary>
    /// Gets or sets the expired-but-unprocessed saga count above which the saga timeout backlog check
    /// reports <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded"/>.
    /// Defaults to <c>10</c>.
    /// </summary>
    public int SagaTimeoutBacklogThreshold { get; set; } = 10;
}
