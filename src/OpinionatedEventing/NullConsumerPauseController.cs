#nullable enable

namespace OpinionatedEventing;

/// <summary>
/// No-op <see cref="IConsumerPauseController"/> that is never paused — consumers always run at full speed.
/// </summary>
internal sealed class NullConsumerPauseController : IConsumerPauseController
{
    /// <inheritdoc/>
    public bool IsPaused => false;

    /// <inheritdoc/>
    public Task WhenStateChangedAsync(CancellationToken cancellationToken)
        => Task.Delay(Timeout.Infinite, cancellationToken);
}
