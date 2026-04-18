#nullable enable

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpinionatedEventing.DependencyInjection;
using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.Testing;

/// <summary>
/// Fluent builder that constructs a minimal <see cref="IServiceProvider"/> pre-wired with
/// OpinionatedEventing fakes, ready for unit testing without any broker or database.
/// Not for production use.
/// </summary>
/// <remarks>
/// Registers <see cref="FakePublisher"/> as <see cref="IPublisher"/>,
/// <see cref="FakeMessagingContext"/> as <see cref="IMessagingContext"/>, and
/// <see cref="InMemoryOutboxStore"/> as <see cref="IOutboxStore"/>.
/// Logging is wired to <see cref="NullLoggerFactory"/> so no log output is produced.
/// </remarks>
public sealed class TestMessagingBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private readonly OpinionatedEventingBuilder _builder;
    private bool _built;

    /// <summary>Gets the <see cref="FakePublisher"/> registered in the container.</summary>
    public FakePublisher Publisher { get; } = new();

    /// <summary>Gets the <see cref="FakeMessagingContext"/> registered in the container.</summary>
    public FakeMessagingContext MessagingContext { get; } = new();

    /// <summary>Gets the <see cref="InMemoryOutboxStore"/> registered in the container.</summary>
    public InMemoryOutboxStore OutboxStore { get; } = new();

    /// <summary>Initialises a new builder with fakes and null logging pre-registered.</summary>
    public TestMessagingBuilder()
    {
        _services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        _services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Register fakes before AddOpinionatedEventing so the TryAdd calls don't overwrite them.
        _services.AddSingleton<IPublisher>(Publisher);
        _services.AddSingleton<IOutboxStore>(OutboxStore);
        _services.AddScoped<IMessagingContext>(_ => MessagingContext);

        _builder = _services.AddOpinionatedEventing();
    }

    /// <summary>
    /// Scans <paramref name="assemblies"/> for <see cref="IEventHandler{TEvent}"/> and
    /// <see cref="ICommandHandler{TCommand}"/> implementations and registers them in DI.
    /// </summary>
    /// <param name="assemblies">Assemblies to scan for handler types.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public TestMessagingBuilder AddHandlersFromAssemblies(params Assembly[] assemblies)
    {
        _builder.AddHandlersFromAssemblies(assemblies);
        return this;
    }

    /// <summary>
    /// Builds and returns the configured <see cref="IServiceProvider"/>.
    /// The caller is responsible for disposing the returned provider.
    /// This method may only be called once per builder instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if called more than once.</exception>
    public IServiceProvider Build()
    {
        if (_built)
            throw new InvalidOperationException("Build() has already been called on this TestMessagingBuilder instance.");

        _built = true;
        return _services.BuildServiceProvider();
    }
}
