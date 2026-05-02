#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpinionatedEventing;
using OpinionatedEventing.DependencyInjection;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.RabbitMQ;
using RabbitMQ.Client;
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

    [Fact]
    public void AddRabbitMQTransport_does_not_register_IConnection_singleton()
    {
        // IConnection must not be registered as a singleton — doing so would require
        // sync-over-async at DI resolution time (see issue #104).
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddRabbitMQTransport(o => o.ConnectionString = "amqp://guest:guest@localhost:5672/");

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConnection));
        Assert.Null(descriptor);
    }

    [Fact]
    public void AddRabbitMQTransport_registers_RabbitMqConnectionHolder()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddRabbitMQTransport(o => o.ConnectionString = "amqp://guest:guest@localhost:5672/");

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(RabbitMqConnectionHolder));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddRabbitMQTransport_registers_RabbitMqConnectionInitializer_as_IHostedService()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();
        services.AddRabbitMQTransport(o => o.ConnectionString = "amqp://guest:guest@localhost:5672/");

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(RabbitMqConnectionInitializer));
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void Handler_registered_after_AddRabbitMQTransport_appears_in_registry()
    {
        var services = new ServiceCollection();
        var builder = services.AddOpinionatedEventing();

        // Transport is called first — registry must still capture handlers registered after this.
        services.AddRabbitMQTransport(o => o.ConnectionString = "amqp://guest:guest@localhost:5672/");

        builder.AddHandlersFromAssemblies(typeof(RegistrationTests).Assembly);

        // Read the registry directly from the descriptor — avoids building the provider,
        // which would reject the open-generic CapturingEventHandler<T> in the integration tests.
        var registry = services
            .FirstOrDefault(d => d.ImplementationInstance is MessageHandlerRegistry)
            ?.ImplementationInstance as MessageHandlerRegistry;
        Assert.NotNull(registry);
        Assert.Contains(typeof(RmqTestEvent), registry.EventTypes);
        Assert.Contains(typeof(RmqTestCommand), registry.CommandTypes);
    }

    // ---- test fakes ----

    public sealed record RmqTestEvent(Guid Id) : IEvent;
    public sealed record RmqTestCommand(Guid Id) : ICommand;

    public sealed class RmqTestEventHandler : IEventHandler<RmqTestEvent>
    {
        public Task HandleAsync(RmqTestEvent @event, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public sealed class RmqTestCommandHandler : ICommandHandler<RmqTestCommand>
    {
        public Task HandleAsync(RmqTestCommand command, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
