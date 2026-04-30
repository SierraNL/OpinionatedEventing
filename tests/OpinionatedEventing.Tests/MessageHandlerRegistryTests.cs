using Microsoft.Extensions.DependencyInjection;
using OpinionatedEventing.DependencyInjection;
using Xunit;

namespace OpinionatedEventing.Tests;

public sealed class MessageHandlerRegistryTests
{
    [Fact]
    public void AddOpinionatedEventing_registers_MessageHandlerRegistry()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();

        var sp = services.BuildServiceProvider();
        var registry = sp.GetService<MessageHandlerRegistry>();
        Assert.NotNull(registry);
    }

    [Fact]
    public void AddHandlersFromAssemblies_populates_EventTypes()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing()
                .AddHandlersFromAssemblies(typeof(MessageHandlerRegistryTests).Assembly);

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<MessageHandlerRegistry>();

        Assert.Contains(typeof(RegistryTestEvent), registry.EventTypes);
    }

    [Fact]
    public void AddHandlersFromAssemblies_populates_CommandTypes()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing()
                .AddHandlersFromAssemblies(typeof(MessageHandlerRegistryTests).Assembly);

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<MessageHandlerRegistry>();

        Assert.Contains(typeof(RegistryTestCommand), registry.CommandTypes);
    }

    [Fact]
    public void EventType_registered_idempotently_when_same_assembly_scanned_twice()
    {
        var services = new ServiceCollection();
        var builder = services.AddOpinionatedEventing()
                              .AddHandlersFromAssemblies(typeof(MessageHandlerRegistryTests).Assembly);
        builder.AddHandlersFromAssemblies(typeof(MessageHandlerRegistryTests).Assembly);

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<MessageHandlerRegistry>();

        Assert.Single(registry.EventTypes, t => t == typeof(RegistryTestEvent));
    }

    [Fact]
    public void AddOpinionatedEventing_called_twice_shares_same_registry_instance()
    {
        var services = new ServiceCollection();
        var builder1 = services.AddOpinionatedEventing();
        builder1.AddHandlersFromAssemblies(typeof(MessageHandlerRegistryTests).Assembly);

        // Second call must retrieve the already-registered instance, not create a new one.
        services.AddOpinionatedEventing();

        var registry = services
            .FirstOrDefault(d => d.ImplementationInstance is MessageHandlerRegistry)
            ?.ImplementationInstance as MessageHandlerRegistry;

        Assert.NotNull(registry);
        Assert.Contains(typeof(RegistryTestEvent), registry.EventTypes);
        Assert.Contains(typeof(RegistryTestCommand), registry.CommandTypes);
    }

    // ---- test fakes ----

    public sealed record RegistryTestEvent(Guid Id) : IEvent;
    public sealed record RegistryTestCommand(Guid Id) : ICommand;

    public sealed class RegistryTestEventHandler : IEventHandler<RegistryTestEvent>
    {
        public Task HandleAsync(RegistryTestEvent @event, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public sealed class RegistryTestCommandHandler : ICommandHandler<RegistryTestCommand>
    {
        public Task HandleAsync(RegistryTestCommand command, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
