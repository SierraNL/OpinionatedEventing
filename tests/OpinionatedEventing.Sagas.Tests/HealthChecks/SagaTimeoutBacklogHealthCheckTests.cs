#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Sagas;
using OpinionatedEventing.Sagas.HealthChecks;
using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Sagas.Tests.HealthChecks;

public sealed class SagaTimeoutBacklogHealthCheckTests
{
    private static async Task<HealthReportEntry> RunSagaCheckAsync(IServiceProvider sp, CancellationToken ct)
    {
        var healthService = sp.GetRequiredService<HealthCheckService>();
        var report = await healthService.CheckHealthAsync(
            r => r.Name == "opinionatedeventing-saga-timeout-backlog", ct);
        return report.Entries["opinionatedeventing-saga-timeout-backlog"];
    }

    private static InMemorySagaStateStore StoreWithExpiredSagas(int count, DateTimeOffset now)
    {
        var store = new InMemorySagaStateStore();
        for (var i = 0; i < count; i++)
        {
            store.SaveAsync(new SagaState
            {
                SagaType = "TestSaga",
                CorrelationId = Guid.NewGuid().ToString(),
                Status = SagaStatus.Active,
                ExpiresAt = now.AddMinutes(-1),
                State = "{}",
            }).GetAwaiter().GetResult();
        }
        return store;
    }

    [Fact]
    public void AddSagaTimeoutBacklogHealthCheck_registers_check()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddSagaTimeoutBacklogHealthCheck();

        var sp = services.BuildServiceProvider();
        var registrations = sp.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        Assert.Contains(registrations, r => r.Name == "opinionatedeventing-saga-timeout-backlog");
    }

    [Fact]
    public void AddSagaTimeoutBacklogHealthCheck_uses_default_options()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddSagaTimeoutBacklogHealthCheck();

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<SagaHealthCheckOptions>>().Value;

        Assert.Equal(10, opts.TimeoutBacklogThreshold);
    }

    [Fact]
    public void AddSagaTimeoutBacklogHealthCheck_applies_custom_options()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddSagaTimeoutBacklogHealthCheck(o => o.TimeoutBacklogThreshold = 5);

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<SagaHealthCheckOptions>>().Value;

        Assert.Equal(5, opts.TimeoutBacklogThreshold);
    }

    [Fact]
    public async Task Returns_Healthy_when_expired_sagas_below_threshold()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISagaStateStore>(StoreWithExpiredSagas(5, now));
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(now));
        services.AddHealthChecks().AddSagaTimeoutBacklogHealthCheck(o => o.TimeoutBacklogThreshold = 10);
        var sp = services.BuildServiceProvider();

        var entry = await RunSagaCheckAsync(sp, ct);

        Assert.Equal(HealthStatus.Healthy, entry.Status);
    }

    [Fact]
    public async Task Returns_Degraded_when_expired_sagas_exceed_threshold()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISagaStateStore>(StoreWithExpiredSagas(15, now));
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(now));
        services.AddHealthChecks().AddSagaTimeoutBacklogHealthCheck(o => o.TimeoutBacklogThreshold = 10);
        var sp = services.BuildServiceProvider();

        var entry = await RunSagaCheckAsync(sp, ct);

        Assert.Equal(HealthStatus.Degraded, entry.Status);
    }

    [Fact]
    public async Task Returns_Healthy_when_ISagaStateStore_not_registered()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddSagaTimeoutBacklogHealthCheck();
        var sp = services.BuildServiceProvider();

        var entry = await RunSagaCheckAsync(sp, ct);

        Assert.Equal(HealthStatus.Healthy, entry.Status);
    }
}
