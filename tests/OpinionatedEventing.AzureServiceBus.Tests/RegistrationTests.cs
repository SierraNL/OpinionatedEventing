#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpinionatedEventing;
using OpinionatedEventing.AzureServiceBus;
using OpinionatedEventing.DependencyInjection;
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
    public void AddAzureServiceBusTransport_registers_default_IConsumerPauseController()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddAzureServiceBusTransport(o => o.ConnectionString =
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");

        // Default controller is registered and never paused
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConsumerPauseController));
        Assert.NotNull(descriptor);
        var controller = services.BuildServiceProvider().GetRequiredService<IConsumerPauseController>();
        Assert.False(controller.IsPaused);
    }

    [Fact]
    public void AddAzureServiceBusTransport_IConsumerPauseController_can_be_overridden()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddSingleton<IConsumerPauseController, FakeConsumerPauseController>();
        services.AddAzureServiceBusTransport(o => o.ConnectionString =
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");

        var sp = services.BuildServiceProvider();
        Assert.IsType<FakeConsumerPauseController>(sp.GetRequiredService<IConsumerPauseController>());
    }

    [Fact]
    public void Handler_registered_after_AddAzureServiceBusTransport_appears_in_registry()
    {
        var services = new ServiceCollection();
        var builder = services.AddOpinionatedEventing();

        // Transport is called first — registry must still capture handlers registered after this.
        services.AddAzureServiceBusTransport(o =>
            o.ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");

        builder.AddHandlersFromAssemblies(typeof(RegistrationTests).Assembly);

        // Read the registry directly from the descriptor — avoids building the provider,
        // which would reject the open-generic CapturingEventHandler<T> in the integration tests.
        var registry = services
            .FirstOrDefault(d => d.ImplementationInstance is MessageHandlerRegistry)
            ?.ImplementationInstance as MessageHandlerRegistry;
        Assert.NotNull(registry);
        Assert.Contains(typeof(AsbTestEvent), registry.EventTypes);
        Assert.Contains(typeof(AsbTestCommand), registry.CommandTypes);
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

    // ---- test fakes ----

    public sealed record AsbTestEvent(Guid Id) : IEvent;
    public sealed record AsbTestCommand(Guid Id) : ICommand;

    public sealed class AsbTestEventHandler : IEventHandler<AsbTestEvent>
    {
        public Task HandleAsync(AsbTestEvent @event, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public sealed class AsbTestCommandHandler : ICommandHandler<AsbTestCommand>
    {
        public Task HandleAsync(AsbTestCommand command, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
