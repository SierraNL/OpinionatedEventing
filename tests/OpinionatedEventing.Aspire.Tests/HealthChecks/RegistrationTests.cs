#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Aspire.HealthChecks;
using Xunit;

namespace OpinionatedEventing.Aspire.Tests.HealthChecks;

public sealed class RegistrationTests
{
    [Fact]
    public void AddOpinionatedEventingHealthChecks_registers_three_checks()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddOpinionatedEventingHealthChecks();

        var sp = services.BuildServiceProvider();
        var registrations = sp.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        Assert.Contains(registrations, r => r.Name == "opinionatedeventing-broker");
        Assert.Contains(registrations, r => r.Name == "opinionatedeventing-outbox-backlog");
        Assert.Contains(registrations, r => r.Name == "opinionatedeventing-saga-timeout-backlog");
    }

    [Fact]
    public void AddOpinionatedEventingHealthChecks_uses_default_options()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddOpinionatedEventingHealthChecks();

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<OpinionatedEventingHealthCheckOptions>>().Value;

        Assert.Equal(100, opts.OutboxBacklogThreshold);
        Assert.Equal(10, opts.SagaTimeoutBacklogThreshold);
    }

    [Fact]
    public void AddOpinionatedEventingHealthChecks_applies_custom_options()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddOpinionatedEventingHealthChecks(o =>
        {
            o.OutboxBacklogThreshold = 50;
            o.SagaTimeoutBacklogThreshold = 5;
        });

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<OpinionatedEventingHealthCheckOptions>>().Value;

        Assert.Equal(50, opts.OutboxBacklogThreshold);
        Assert.Equal(5, opts.SagaTimeoutBacklogThreshold);
    }
}
