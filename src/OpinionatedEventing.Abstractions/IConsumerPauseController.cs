#nullable enable

namespace OpinionatedEventing;

/// <summary>
/// Controls whether broker consumer workers should pause accepting new messages.
/// </summary>
/// <remarks>
/// The default implementation (<c>NullConsumerPauseController</c>) never pauses.
/// Register <c>HealthCheckConsumerPauseController</c> via
/// <c>AddOpinionatedEventingHealthChecks().WithConsumerPause()</c> to pause consumers
/// automatically when readiness probes become unhealthy.
/// </remarks>
public interface IConsumerPauseController
{
    /// <summary>Gets a value indicating whether consumers should pause accepting messages.</summary>
    bool IsPaused { get; }

    /// <summary>
    /// Returns a <see cref="Task"/> that completes when <see cref="IsPaused"/> transitions
    /// to a new value, or when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the wait.</param>
    Task WhenStateChangedAsync(CancellationToken cancellationToken);
}
