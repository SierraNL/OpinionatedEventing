#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpinionatedEventing;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.RabbitMQ;
using Xunit;

namespace OpinionatedEventing.RabbitMQ.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void AddRabbitMQTransport_registers_ITransport()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddRabbitMQTransport(o =>
        {
            o.ConnectionString = "amqp://guest:guest@localhost:5672/";
            o.ServiceName = "test-service";
        });

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ITransport));
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void AddRabbitMQTransport_registers_default_IConsumerPauseController()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddRabbitMQTransport(o => o.ConnectionString = "amqp://guest:guest@localhost:5672/");

        // Default controller is registered and never paused
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConsumerPauseController));
        Assert.NotNull(descriptor);
        var controller = services.BuildServiceProvider().GetRequiredService<IConsumerPauseController>();
        Assert.False(controller.IsPaused);
    }

    [Fact]
    public void AddRabbitMQTransport_IConsumerPauseController_can_be_overridden()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        // Register a custom controller BEFORE the transport — TryAdd skips the default
        services.AddSingleton<IConsumerPauseController, FakeConsumerPauseController>();
        services.AddRabbitMQTransport(o => o.ConnectionString = "amqp://guest:guest@localhost:5672/");

        var sp = services.BuildServiceProvider();
        Assert.IsType<FakeConsumerPauseController>(sp.GetRequiredService<IConsumerPauseController>());
    }

    [Fact]
    public void AddRabbitMQTransport_configures_options()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddRabbitMQTransport(o =>
        {
            o.ConnectionString = "amqp://guest:guest@localhost:5672/";
            o.ServiceName = "order-service";
            o.AutoDeclareTopology = false;
            o.PrefetchCount = 5;
        });

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<RabbitMQOptions>>().Value;

        Assert.Equal("order-service", opts.ServiceName);
        Assert.False(opts.AutoDeclareTopology);
        Assert.Equal(5, opts.PrefetchCount);
    }
}
