#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpinionatedEventing.AzureServiceBus;
using OpinionatedEventing.Outbox;
using Xunit;

namespace OpinionatedEventing.AzureServiceBus.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void AddAzureServiceBusTransport_registers_ITransport()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddAzureServiceBusTransport(o =>
        {
            o.ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
            o.ServiceName = "test-service";
        });

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ITransport));
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void AddAzureServiceBusTransport_configures_options()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddAzureServiceBusTransport(o =>
        {
            o.ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
            o.ServiceName = "order-service";
            o.AutoCreateResources = true;
            o.EnableSessions = false;
            o.MaxDeliveryCount = 3;
        });

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<AzureServiceBusOptions>>().Value;

        Assert.Equal("order-service", opts.ServiceName);
        Assert.True(opts.AutoCreateResources);
        Assert.Equal(3, opts.MaxDeliveryCount);
    }
}
