#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpinionatedEventing.AzureServiceBus;
using OpinionatedEventing.CloudEvents;
using OpinionatedEventing.CloudEvents.AzureServiceBus;
using OpinionatedEventing.DependencyInjection;
using Xunit;

namespace OpinionatedEventing.CloudEvents.AzureServiceBus.Tests;

public sealed class RegistrationTests
{
    private const string ConnectionString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;" +
        "SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    [Fact]
    public void UseCloudEventsEnvelope_replaces_default_envelope_with_CloudEvents_envelope()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddAzureServiceBusTransport(o => o.ConnectionString = ConnectionString)
            .UseCloudEventsEnvelope(o => o.Source = new Uri("urn:order-service"));

        var sp = services.BuildServiceProvider();
        var envelope = sp.GetRequiredService<IServiceBusMessageEnvelope>();

        Assert.IsType<CloudEventsServiceBusMessageEnvelope>(envelope);
    }

    [Fact]
    public void UseCloudEventsEnvelope_configures_CloudEventsOptions()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddAzureServiceBusTransport(o => o.ConnectionString = ConnectionString)
            .UseCloudEventsEnvelope(o => o.Source = new Uri("urn:order-service"));

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<CloudEventsOptions>>().Value;

        Assert.Equal(new Uri("urn:order-service"), options.Source);
    }

    [Fact]
    public void Without_UseCloudEventsEnvelope_default_envelope_remains_registered()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddAzureServiceBusTransport(o => o.ConnectionString = ConnectionString);

        var sp = services.BuildServiceProvider();
        var envelope = sp.GetRequiredService<IServiceBusMessageEnvelope>();

        Assert.IsType<DefaultServiceBusMessageEnvelope>(envelope);
    }
}
