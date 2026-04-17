#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Aspire.Tests.HealthChecks;

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
    public async Task Returns_Healthy_when_pending_below_threshold()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOutboxMonitor>(new FakeOutboxMonitor { PendingCount = 50 });
        services.AddHealthChecks().AddOpinionatedEventingHealthChecks(o => o.OutboxBacklogThreshold = 100);
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
        services.AddHealthChecks().AddOpinionatedEventingHealthChecks(o => o.OutboxBacklogThreshold = 100);
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
        services.AddHealthChecks().AddOpinionatedEventingHealthChecks();
        var sp = services.BuildServiceProvider();

        var entry = await RunOutboxCheckAsync(sp, ct);

        Assert.Equal(HealthStatus.Healthy, entry.Status);
    }
}
