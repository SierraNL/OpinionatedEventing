#nullable enable

using Xunit;

namespace OpinionatedEventing.Core.Tests;

public sealed class NullConsumerPauseControllerTests
{
    [Fact]
    public void IsPaused_is_always_false()
    {
        var controller = new NullConsumerPauseController();
        Assert.False(controller.IsPaused);
    }

    [Fact]
    public async Task WhenStateChangedAsync_completes_when_cancellation_requested()
    {
        using var cts = new CancellationTokenSource();
        var controller = new NullConsumerPauseController();

        var waitTask = controller.WhenStateChangedAsync(cts.Token);
        Assert.False(waitTask.IsCompleted);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);
    }

    [Fact]
    public async Task WhenStateChangedAsync_never_completes_without_cancellation()
    {
        var controller = new NullConsumerPauseController();
        using var cts = new CancellationTokenSource(millisecondsDelay: 50);

        // Should only complete because of the timeout — not because state changed
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.WhenStateChangedAsync(cts.Token));
    }
}
