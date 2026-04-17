#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpinionatedEventing.Sagas;
using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Aspire.Tests.HealthChecks;

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
    public async Task Returns_Healthy_when_expired_sagas_below_threshold()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var fakeTime = new FakeTimeProvider(now);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISagaStateStore>(StoreWithExpiredSagas(5, now));
        services.AddSingleton<TimeProvider>(fakeTime);
        services.AddHealthChecks().AddOpinionatedEventingHealthChecks(o => o.SagaTimeoutBacklogThreshold = 10);
        var sp = services.BuildServiceProvider();

        var entry = await RunSagaCheckAsync(sp, ct);

        Assert.Equal(HealthStatus.Healthy, entry.Status);
    }

    [Fact]
    public async Task Returns_Degraded_when_expired_sagas_exceed_threshold()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var fakeTime = new FakeTimeProvider(now);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISagaStateStore>(StoreWithExpiredSagas(15, now));
        services.AddSingleton<TimeProvider>(fakeTime);
        services.AddHealthChecks().AddOpinionatedEventingHealthChecks(o => o.SagaTimeoutBacklogThreshold = 10);
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
        services.AddHealthChecks().AddOpinionatedEventingHealthChecks();
        var sp = services.BuildServiceProvider();

        var entry = await RunSagaCheckAsync(sp, ct);

        Assert.Equal(HealthStatus.Healthy, entry.Status);
    }
}
