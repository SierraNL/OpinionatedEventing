#nullable enable

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpinionatedEventing;

namespace OpinionatedEventing.Aspire.HealthChecks;

/// <summary>
/// Pauses broker consumer workers when any readiness health check reports
/// <see cref="HealthStatus.Degraded"/> or <see cref="HealthStatus.Unhealthy"/>,
/// and resumes them when all readiness checks recover to <see cref="HealthStatus.Healthy"/>.
/// </summary>
/// <remarks>
/// Register via <c>AddOpinionatedEventingHealthChecks().WithConsumerPause()</c>.
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
    /// Evaluates the health report and pauses or resumes consumers based on readiness check results.
    /// </summary>
    /// <param name="report">The health report published by the health check framework.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        var shouldPause = report.Entries.Any(e =>
            e.Value.Tags.Contains("ready") &&
            e.Value.Status != HealthStatus.Healthy);

        if (shouldPause && !_isPaused)
        {
            _isPaused = true;
            _logger.LogWarning("Readiness probes unhealthy — pausing broker consumers.");
            SignalStateChanged();
        }
        else if (!shouldPause && _isPaused)
        {
            _isPaused = false;
            _logger.LogInformation("Readiness probes recovered — resuming broker consumers.");
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
