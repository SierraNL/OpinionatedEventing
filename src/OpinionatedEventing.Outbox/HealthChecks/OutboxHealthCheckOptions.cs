#nullable enable

namespace OpinionatedEventing.Outbox.HealthChecks;

/// <summary>
/// Configures thresholds for <c>AddOutboxBacklogHealthCheck()</c>.
/// </summary>
public sealed class OutboxHealthCheckOptions
{
    /// <summary>
    /// Gets or sets the pending-message count above which the outbox backlog check reports
    /// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded"/>.
    /// Defaults to <c>100</c>.
    /// </summary>
    public int BacklogThreshold { get; set; } = 100;
}
