using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Sagas;
using OpinionatedEventing.Testing;
using Reqnroll;
using Xunit;

namespace OpinionatedEventing.Aspire.Specs.StepDefinitions;

[Binding]
public sealed class AspireSteps
{
    private readonly ServiceCollection _services = new();
    private HealthReportEntry _result;

    // ── Given steps ──────────────────────────────────────────────────────────

    [Given("the outbox has (\\d+) pending messages")]
    public void GivenOutboxHasPendingMessages(int count)
    {
        _services.AddSingleton<IOutboxMonitor>(new FakeOutboxMonitor { PendingCount = count });
    }

    [Given("the outbox backlog threshold is (\\d+)")]
    public void GivenOutboxBacklogThreshold(int threshold)
    {
        _services.AddHealthChecks().AddOpinionatedEventingHealthChecks(
            o => o.OutboxBacklogThreshold = threshold);
    }

    [Given("no IOutboxMonitor is registered")]
    public void GivenNoOutboxMonitor()
    {
        _services.AddHealthChecks().AddOpinionatedEventingHealthChecks();
    }

    [Given("there are (\\d+) expired sagas")]
    public void GivenExpiredSagas(int count)
    {
        var now = DateTimeOffset.UtcNow;
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
        _services.AddSingleton<ISagaStateStore>(store);
        _services.AddSingleton<TimeProvider>(new FakeTimeProvider(now));
    }

    [Given("the saga timeout backlog threshold is (\\d+)")]
    public void GivenSagaTimeoutBacklogThreshold(int threshold)
    {
        _services.AddHealthChecks().AddOpinionatedEventingHealthChecks(
            o => o.SagaTimeoutBacklogThreshold = threshold);
    }

    [Given("no broker transport is registered")]
    public void GivenNoBrokerTransport()
    {
        _services.AddHealthChecks().AddOpinionatedEventingHealthChecks();
    }

    // ── When steps ───────────────────────────────────────────────────────────

    [When("the outbox backlog health check runs")]
    public async Task WhenOutboxBacklogHealthCheckRuns()
    {
        _services.AddLogging();
        var sp = _services.BuildServiceProvider();
        var svc = sp.GetRequiredService<HealthCheckService>();
        var report = await svc.CheckHealthAsync(r => r.Name == "opinionatedeventing-outbox-backlog");
        _result = report.Entries["opinionatedeventing-outbox-backlog"];
    }

    [When("the saga timeout backlog health check runs")]
    public async Task WhenSagaTimeoutBacklogHealthCheckRuns()
    {
        _services.AddLogging();
        var sp = _services.BuildServiceProvider();
        var svc = sp.GetRequiredService<HealthCheckService>();
        var report = await svc.CheckHealthAsync(r => r.Name == "opinionatedeventing-saga-timeout-backlog");
        _result = report.Entries["opinionatedeventing-saga-timeout-backlog"];
    }

    [When("the broker connectivity health check runs")]
    public async Task WhenBrokerConnectivityHealthCheckRuns()
    {
        _services.AddLogging();
        var sp = _services.BuildServiceProvider();
        var svc = sp.GetRequiredService<HealthCheckService>();
        var report = await svc.CheckHealthAsync(r => r.Name == "opinionatedeventing-broker");
        _result = report.Entries["opinionatedeventing-broker"];
    }

    // ── Then steps ───────────────────────────────────────────────────────────

    [Then("the result is Healthy")]
    public void ThenResultIsHealthy() => Assert.Equal(HealthStatus.Healthy, _result.Status);

    [Then("the result is Degraded")]
    public void ThenResultIsDegraded() => Assert.Equal(HealthStatus.Degraded, _result.Status);
}
