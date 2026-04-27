#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Xunit;

namespace OpinionatedEventing.RabbitMQ.Tests.HealthChecks;

public sealed class RabbitMqConnectivityHealthCheckTests
{
    [Fact]
    public void AddRabbitMqConnectivityHealthCheck_registers_check()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddRabbitMqConnectivityHealthCheck();

        var sp = services.BuildServiceProvider();
        var registrations = sp.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        Assert.Contains(registrations, r => r.Name == "opinionatedeventing-rabbitmq");
    }

    [Fact]
    public void AddRabbitMqConnectivityHealthCheck_check_has_live_and_broker_tags()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddRabbitMqConnectivityHealthCheck();

        var sp = services.BuildServiceProvider();
        var registration = sp.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(r => r.Name == "opinionatedeventing-rabbitmq");

        Assert.Contains("live", registration.Tags);
        Assert.Contains("broker", registration.Tags);
    }
}
