#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpinionatedEventing.Sagas;
using OpinionatedEventing.Testing;
using Reqnroll;
using Xunit;

namespace OpinionatedEventing.Sagas.Specs.StepDefinitions;

[Binding]
public sealed class SagaHealthCheckSteps
{
    private readonly ServiceCollection _services = new();
    private HealthReportEntry _result;

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
        _services.AddHealthChecks().AddSagaTimeoutBacklogHealthCheck(
            o => o.TimeoutBacklogThreshold = threshold);
    }

    [Given("no ISagaStateStore is registered")]
    public void GivenNoSagaStateStore()
    {
        _services.AddHealthChecks().AddSagaTimeoutBacklogHealthCheck();
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

    [Then("the result is Healthy")]
    public void ThenResultIsHealthy() => Assert.Equal(HealthStatus.Healthy, _result.Status);

    [Then("the result is Degraded")]
    public void ThenResultIsDegraded() => Assert.Equal(HealthStatus.Degraded, _result.Status);
}
