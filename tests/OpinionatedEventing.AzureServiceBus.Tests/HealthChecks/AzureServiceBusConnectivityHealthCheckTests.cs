#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace OpinionatedEventing.AzureServiceBus.Tests.HealthChecks;

public sealed class AzureServiceBusConnectivityHealthCheckTests
{
    [Fact]
    public void AddAzureServiceBusConnectivityHealthCheck_registers_check()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddAzureServiceBusConnectivityHealthCheck();

        var sp = services.BuildServiceProvider();
        var registrations = sp.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        Assert.Contains(registrations, r => r.Name == "opinionatedeventing-azureservicebus");
    }

    [Fact]
    public void AddAzureServiceBusConnectivityHealthCheck_check_has_live_and_broker_tags()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddAzureServiceBusConnectivityHealthCheck();

        var sp = services.BuildServiceProvider();
        var registration = sp.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(r => r.Name == "opinionatedeventing-azureservicebus");

        Assert.Contains("live", registration.Tags);
        Assert.Contains("broker", registration.Tags);
    }
}
