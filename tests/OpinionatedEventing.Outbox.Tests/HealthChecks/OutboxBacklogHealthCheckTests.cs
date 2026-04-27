#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Outbox.HealthChecks;
using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Outbox.Tests.HealthChecks;

public sealed class OutboxBacklogHealthCheckTests
{
    private static async Task<HealthReportEntry> RunOutboxCheckAsync(IServiceProvider sp, CancellationToken ct)
    {
        var healthService = sp.GetRequiredService<HealthCheckService>();
        var report = await healthService.CheckHealthAsync(
            r => r.Name == "opinionatedeventing-outbox-backlog", ct);
        return report.Entries["opinionatedeventing-outbox-backlog"];
    }

    [Fact]
    public void AddOutboxBacklogHealthCheck_registers_check()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddOutboxBacklogHealthCheck();

        var sp = services.BuildServiceProvider();
        var registrations = sp.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        Assert.Contains(registrations, r => r.Name == "opinionatedeventing-outbox-backlog");
    }

    [Fact]
    public void AddOutboxBacklogHealthCheck_uses_default_options()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddOutboxBacklogHealthCheck();

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<OutboxHealthCheckOptions>>().Value;

        Assert.Equal(100, opts.BacklogThreshold);
    }

    [Fact]
    public void AddOutboxBacklogHealthCheck_applies_custom_options()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddOutboxBacklogHealthCheck(o => o.BacklogThreshold = 50);

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<OutboxHealthCheckOptions>>().Value;

        Assert.Equal(50, opts.BacklogThreshold);
    }

    [Fact]
    public async Task Returns_Healthy_when_pending_below_threshold()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOutboxMonitor>(new FakeOutboxMonitor { PendingCount = 50 });
        services.AddHealthChecks().AddOutboxBacklogHealthCheck(o => o.BacklogThreshold = 100);
        var sp = services.BuildServiceProvider();

        var entry = await RunOutboxCheckAsync(sp, ct);

        Assert.Equal(HealthStatus.Healthy, entry.Status);
    }

    [Fact]
    public async Task Returns_Degraded_when_pending_exceeds_threshold()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOutboxMonitor>(new FakeOutboxMonitor { PendingCount = 101 });
        services.AddHealthChecks().AddOutboxBacklogHealthCheck(o => o.BacklogThreshold = 100);
        var sp = services.BuildServiceProvider();

        var entry = await RunOutboxCheckAsync(sp, ct);

        Assert.Equal(HealthStatus.Degraded, entry.Status);
    }

    [Fact]
    public async Task Returns_Healthy_when_IOutboxMonitor_not_registered()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddOutboxBacklogHealthCheck();
        var sp = services.BuildServiceProvider();

        var entry = await RunOutboxCheckAsync(sp, ct);

        Assert.Equal(HealthStatus.Healthy, entry.Status);
    }
}
