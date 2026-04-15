using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpinionatedEventing.DependencyInjection;
using OpinionatedEventing.Options;
using Xunit;

namespace OpinionatedEventing.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOpinionatedEventing_RegistersIMessagingContext()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetService<IMessagingContext>();
        Assert.NotNull(context);
    }

    [Fact]
    public void AddOpinionatedEventing_MessagingContextIsScopedSingleton()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var a = scope.ServiceProvider.GetRequiredService<IMessagingContext>();
        var b = scope.ServiceProvider.GetRequiredService<IMessagingContext>();

        Assert.Same(a, b);
    }

    [Fact]
    public void AddOpinionatedEventing_MessagingContextIsDifferentAcrossScopes()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing();

        var provider = services.BuildServiceProvider();

        IMessagingContext first;
        IMessagingContext second;

        using (var scope1 = provider.CreateScope())
            first = scope1.ServiceProvider.GetRequiredService<IMessagingContext>();

        using (var scope2 = provider.CreateScope())
            second = scope2.ServiceProvider.GetRequiredService<IMessagingContext>();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void AddOpinionatedEventing_ConfiguresOptions()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing(options =>
        {
            options.Outbox.BatchSize = 99;
            options.Outbox.MaxAttempts = 3;
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OpinionatedEventingOptions>>().Value;

        Assert.Equal(99, options.Outbox.BatchSize);
        Assert.Equal(3, options.Outbox.MaxAttempts);
    }

    [Fact]
    public void AddOpinionatedEventing_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddOpinionatedEventing();

        Assert.IsType<OpinionatedEventingBuilder>(builder);
    }

    [Fact]
    public void AddHandlersFromAssemblies_RegistersEventHandlers()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing()
                .AddHandlersFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly);

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IEventHandler<TestEvent>>();
        Assert.Single(handlers);
    }

    [Fact]
    public void AddHandlersFromAssemblies_RegistersCommandHandler()
    {
        var services = new ServiceCollection();
        services.AddOpinionatedEventing()
                .AddHandlersFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly);

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider.GetService<ICommandHandler<TestCommand>>();
        Assert.NotNull(handler);
    }

    [Fact]
    public void AddHandlersFromAssemblies_EventHandlerIsIdempotentWhenCalledTwice()
    {
        var services = new ServiceCollection();
        var builder = services.AddOpinionatedEventing()
                              .AddHandlersFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly);

        // second call with the same assembly must not register a duplicate
        builder.AddHandlersFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly);

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IEventHandler<TestEvent>>();
        Assert.Single(handlers);
    }

    [Fact]
    public void AddHandlersFromAssemblies_ThrowsOnDifferentCommandHandlerForSameCommand()
    {
        var services = new ServiceCollection();
        var builder = services.AddOpinionatedEventing();

        // Pre-register a handler via factory (no ImplementationType) to simulate a different
        // registration already in the container before the assembly scan runs.
        services.AddScoped<ICommandHandler<TestCommand>>(_ => new TestCommandHandler());

        // Scanning now finds TestCommandHandler but sees a non-matching existing registration → throws.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.AddHandlersFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly));

        Assert.Contains("Duplicate ICommandHandler", ex.Message);
    }

    [Fact]
    public void AddHandlersFromAssemblies_SameAssemblyTwiceDoesNotThrowForCommandHandler()
    {
        var services = new ServiceCollection();
        var builder = services.AddOpinionatedEventing()
                              .AddHandlersFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly);

        // calling with the same assembly again must be a no-op, not throw
        builder.AddHandlersFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly);

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetService<ICommandHandler<TestCommand>>();
        Assert.NotNull(handler);
    }

    // ---- test fakes ----

    public sealed record TestEvent(Guid Id) : IEvent;
    public sealed record TestCommand(Guid Id) : ICommand;

    public sealed class TestEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public sealed class TestCommandHandler : ICommandHandler<TestCommand>
    {
        public Task HandleAsync(TestCommand command, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}

