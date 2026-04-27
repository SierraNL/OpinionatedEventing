#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using OpinionatedEventing;
using OpinionatedEventing.HealthChecks;
using Xunit;

namespace OpinionatedEventing.Tests.HealthChecks;

public sealed class HealthCheckConsumerPauseControllerTests
{
    private static HealthCheckConsumerPauseController CreateController()
        => new(NullLogger<HealthCheckConsumerPauseController>.Instance);

    private static HealthReport BuildReport(params (string name, HealthStatus status, string[] tags)[] entries)
    {
        var dict = entries.ToDictionary(
            e => e.name,
            e => new HealthReportEntry(e.status, description: null, TimeSpan.Zero, exception: null, data: null, tags: e.tags));
        return new HealthReport(dict, TimeSpan.Zero);
    }

    [Fact]
    public void IsPaused_is_false_initially()
    {
        var controller = CreateController();
        Assert.False(controller.IsPaused);
    }

    [Fact]
    public async Task PublishAsync_pauses_when_pause_check_is_Degraded()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = CreateController();

        var report = BuildReport(("check1", HealthStatus.Degraded, ["pause"]));
        await controller.PublishAsync(report, ct);

        Assert.True(controller.IsPaused);
    }

    [Fact]
    public async Task PublishAsync_pauses_when_pause_check_is_Unhealthy()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = CreateController();

        var report = BuildReport(("check1", HealthStatus.Unhealthy, ["pause"]));
        await controller.PublishAsync(report, ct);

        Assert.True(controller.IsPaused);
    }

    [Fact]
    public async Task PublishAsync_does_not_pause_for_non_pause_tags()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = CreateController();

        var report = BuildReport(("broker", HealthStatus.Unhealthy, ["live", "broker"]));
        await controller.PublishAsync(report, ct);

        Assert.False(controller.IsPaused);
    }

    [Fact]
    public async Task PublishAsync_does_not_pause_for_ready_tag_without_pause()
    {
        // "ready"-tagged checks (e.g. backlog) must not trigger consumer pause —
        // pausing consumers does not help drain internal backlogs.
        var ct = TestContext.Current.CancellationToken;
        var controller = CreateController();

        var report = BuildReport(
            ("outbox-backlog", HealthStatus.Degraded, ["ready", "outbox"]),
            ("saga-backlog", HealthStatus.Degraded, ["ready", "saga"]));
        await controller.PublishAsync(report, ct);

        Assert.False(controller.IsPaused);
    }

    [Fact]
    public async Task PublishAsync_does_not_pause_when_pause_check_is_Healthy()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = CreateController();

        var report = BuildReport(("check1", HealthStatus.Healthy, ["pause"]));
        await controller.PublishAsync(report, ct);

        Assert.False(controller.IsPaused);
    }

    [Fact]
    public async Task PublishAsync_resumes_after_recovery()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = CreateController();

        await controller.PublishAsync(BuildReport(("check1", HealthStatus.Degraded, ["pause"])), ct);
        Assert.True(controller.IsPaused);

        await controller.PublishAsync(BuildReport(("check1", HealthStatus.Healthy, ["pause"])), ct);
        Assert.False(controller.IsPaused);
    }

    [Fact]
    public async Task WhenStateChangedAsync_completes_when_state_transitions_to_paused()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = CreateController();

        var waitTask = controller.WhenStateChangedAsync(ct);
        Assert.False(waitTask.IsCompleted);

        await controller.PublishAsync(BuildReport(("check1", HealthStatus.Degraded, ["pause"])), ct);

        await waitTask.WaitAsync(TimeSpan.FromSeconds(1), ct);
        Assert.True(waitTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WhenStateChangedAsync_completes_when_state_transitions_to_resumed()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = CreateController();

        await controller.PublishAsync(BuildReport(("check1", HealthStatus.Degraded, ["pause"])), ct);

        var waitTask = controller.WhenStateChangedAsync(ct);
        Assert.False(waitTask.IsCompleted);

        await controller.PublishAsync(BuildReport(("check1", HealthStatus.Healthy, ["pause"])), ct);

        await waitTask.WaitAsync(TimeSpan.FromSeconds(1), ct);
        Assert.True(waitTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WhenStateChangedAsync_cancelled_when_token_is_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var controller = CreateController();

        var waitTask = controller.WhenStateChangedAsync(cts.Token);
        Assert.False(waitTask.IsCompleted);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);
    }

    [Fact]
    public async Task PublishAsync_does_not_signal_when_already_paused()
    {
        var ct = TestContext.Current.CancellationToken;
        var controller = CreateController();

        await controller.PublishAsync(BuildReport(("check1", HealthStatus.Degraded, ["pause"])), ct);

        var waitTask = controller.WhenStateChangedAsync(ct);

        await controller.PublishAsync(BuildReport(("check1", HealthStatus.Unhealthy, ["pause"])), ct);

        Assert.False(waitTask.IsCompleted);
    }

    [Fact]
    public void WithConsumerPause_registers_controller_as_IConsumerPauseController_and_IHealthCheckPublisher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().WithConsumerPause();
        var sp = services.BuildServiceProvider();

        var pauseController = sp.GetRequiredService<IConsumerPauseController>();
        var healthPublisher = sp.GetRequiredService<IHealthCheckPublisher>();

        Assert.IsType<HealthCheckConsumerPauseController>(pauseController);
        Assert.Same(pauseController, healthPublisher);
    }
}
