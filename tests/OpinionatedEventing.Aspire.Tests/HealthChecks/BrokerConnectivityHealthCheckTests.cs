#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace OpinionatedEventing.Aspire.Tests.HealthChecks;

public sealed class BrokerConnectivityHealthCheckTests
{
    [Fact]
    public async Task Returns_Healthy_when_no_broker_registered()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddOpinionatedEventingHealthChecks();
        var sp = services.BuildServiceProvider();

        var healthService = sp.GetRequiredService<HealthCheckService>();
        var report = await healthService.CheckHealthAsync(
            r => r.Name == "opinionatedeventing-broker", ct);

        var entry = report.Entries["opinionatedeventing-broker"];
        Assert.Equal(HealthStatus.Healthy, entry.Status);
    }
}
