#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Testing;
using Reqnroll;
using Xunit;

namespace OpinionatedEventing.Outbox.Specs.StepDefinitions;

[Binding]
public sealed class OutboxHealthCheckSteps
{
    private readonly ServiceCollection _services = new();
    private HealthReportEntry _result;

    [Given("the outbox has (\\d+) pending messages")]
    public void GivenOutboxHasPendingMessages(int count)
    {
        _services.AddSingleton<IOutboxMonitor>(new FakeOutboxMonitor { PendingCount = count });
    }

    [Given("the outbox backlog threshold is (\\d+)")]
    public void GivenOutboxBacklogThreshold(int threshold)
    {
        _services.AddHealthChecks().AddOutboxBacklogHealthCheck(
            o => o.BacklogThreshold = threshold);
    }

    [Given("no IOutboxMonitor is registered")]
    public void GivenNoOutboxMonitor()
    {
        _services.AddHealthChecks().AddOutboxBacklogHealthCheck();
    }

    [When("the outbox backlog health check runs")]
    public async Task WhenOutboxBacklogHealthCheckRuns()
    {
        _services.AddLogging();
        var sp = _services.BuildServiceProvider();
        var svc = sp.GetRequiredService<HealthCheckService>();
        var report = await svc.CheckHealthAsync(r => r.Name == "opinionatedeventing-outbox-backlog");
        _result = report.Entries["opinionatedeventing-outbox-backlog"];
    }

    [Then("the result is Healthy")]
    public void ThenResultIsHealthy() => Assert.Equal(HealthStatus.Healthy, _result.Status);

    [Then("the result is Degraded")]
    public void ThenResultIsDegraded() => Assert.Equal(HealthStatus.Degraded, _result.Status);
}
