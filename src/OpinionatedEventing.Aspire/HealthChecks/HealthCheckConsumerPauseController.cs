#nullable enable

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpinionatedEventing;

namespace OpinionatedEventing.Aspire.HealthChecks;

/// <summary>
/// Pauses broker consumer workers when any health check tagged <c>"pause"</c> reports
/// <see cref="HealthStatus.Degraded"/> or <see cref="HealthStatus.Unhealthy"/>,
/// and resumes them when all such checks recover to <see cref="HealthStatus.Healthy"/>.
/// </summary>
/// <remarks>
/// Register via <c>AddOpinionatedEventingHealthChecks().WithConsumerPause()</c>.
/// Only checks explicitly tagged <c>"pause"</c> influence the pause decision — backlog
/// checks (tagged <c>"ready"</c>) are intentionally excluded because pausing consumers
/// does not help drain internal backlogs.
/// </remarks>
public sealed class HealthCheckConsumerPauseController : IConsumerPauseController, IHealthCheckPublisher
{
    private readonly ILogger<HealthCheckConsumerPauseController> _logger;
    private volatile bool _isPaused;
    private volatile TaskCompletionSource _stateChangedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Initialises a new <see cref="HealthCheckConsumerPauseController"/>.</summary>
    public HealthCheckConsumerPauseController(ILogger<HealthCheckConsumerPauseController> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsPaused => _isPaused;

    /// <inheritdoc/>
    public Task WhenStateChangedAsync(CancellationToken cancellationToken)
        => _stateChangedTcs.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// Evaluates the health report and pauses or resumes consumers based on <c>"pause"</c>-tagged check results.
    /// </summary>
    /// <param name="report">The health report published by the health check framework.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        var shouldPause = report.Entries.Any(e =>
            e.Value.Tags.Contains("pause") &&
            e.Value.Status != HealthStatus.Healthy);

        if (shouldPause && !_isPaused)
        {
            _isPaused = true;
            _logger.LogWarning("Dependency health checks unhealthy — pausing broker consumers.");
            SignalStateChanged();
        }
        else if (!shouldPause && _isPaused)
        {
            _isPaused = false;
            _logger.LogInformation("Dependency health checks recovered — resuming broker consumers.");
            SignalStateChanged();
        }

        return Task.CompletedTask;
    }

    private void SignalStateChanged()
    {
        var old = Interlocked.Exchange(
            ref _stateChangedTcs,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        old.TrySetResult();
    }
}
