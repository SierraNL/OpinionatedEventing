#nullable enable

namespace OpinionatedEventing;

/// <summary>
/// Test double for <see cref="IConsumerPauseController"/> that allows tests to
/// programmatically pause and resume broker consumer workers.
/// </summary>
public sealed class FakeConsumerPauseController : IConsumerPauseController
{
    private volatile bool _isPaused;
    private volatile TaskCompletionSource _stateChangedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Initialises a new <see cref="FakeConsumerPauseController"/>.</summary>
    /// <param name="startPaused">
    /// <see langword="true"/> to start in the paused state; <see langword="false"/> (default) to start unpaused.
    /// </param>
    public FakeConsumerPauseController(bool startPaused = false)
    {
        _isPaused = startPaused;
    }

    /// <inheritdoc/>
    public bool IsPaused => _isPaused;

    /// <inheritdoc/>
    public Task WhenStateChangedAsync(CancellationToken cancellationToken)
        => _stateChangedTcs.Task.WaitAsync(cancellationToken);

    /// <summary>Transitions to the specified pause state, signalling any waiters if the state changed.</summary>
    /// <param name="paused"><see langword="true"/> to pause; <see langword="false"/> to resume.</param>
    public void SetPaused(bool paused)
    {
        if (_isPaused == paused)
            return;

        _isPaused = paused;
        var old = Interlocked.Exchange(
            ref _stateChangedTcs,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        old.TrySetResult();
    }
}
