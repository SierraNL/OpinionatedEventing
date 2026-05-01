#nullable enable

using Microsoft.Extensions.DependencyInjection;

namespace OpinionatedEventing.DependencyInjection;

/// <summary>
/// Holds the set of event and command types that have registered handlers.
/// Populated during DI configuration by <see cref="OpinionatedEventingBuilder.AddHandlersFromAssemblies"/>,
/// <c>AddSaga&lt;TOrchestrator&gt;</c>, and <c>AddSagaParticipant&lt;TParticipant&gt;</c>.
/// Also backfilled at host startup by transport topology initializers to catch handler types that were
/// registered directly on <see cref="IServiceCollection"/> via factory lambdas.
/// Consumed by transport workers and topology initializers at host startup.
/// </summary>
public sealed class MessageHandlerRegistry
{
    // DI registration is single-threaded at startup; no synchronisation needed.
    private readonly HashSet<Type> _eventTypes = new();
    private readonly HashSet<Type> _commandTypes = new();

    /// <summary>All event types for which at least one handler is registered.</summary>
    public IReadOnlyCollection<Type> EventTypes => _eventTypes;

    /// <summary>All command types for which a handler is registered.</summary>
    public IReadOnlyCollection<Type> CommandTypes => _commandTypes;

    internal void RegisterEventType(Type eventType) => _eventTypes.Add(eventType);
    internal void RegisterCommandType(Type commandType) => _commandTypes.Add(commandType);

    /// <summary>
    /// Scans <paramref name="services"/> for any <c>IEventHandler&lt;T&gt;</c> and
    /// <c>ICommandHandler&lt;T&gt;</c> service descriptors and registers their message types.
    /// This is called by transport topology initializers at host startup to capture handler types
    /// that were registered via factory lambdas rather than
    /// <see cref="OpinionatedEventingBuilder.AddHandlersFromAssemblies"/>.
    /// </summary>
    internal void BackfillFromServiceCollection(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var descriptor in services)
        {
            var serviceType = descriptor.ServiceType;
            if (!serviceType.IsGenericType)
                continue;

            var definition = serviceType.GetGenericTypeDefinition();
            var typeArg = serviceType.GetGenericArguments()[0];

            // Skip open generic parameters (FullName is null for unbound type arguments).
            if (typeArg.FullName is null)
                continue;

            if (definition == typeof(IEventHandler<>))
                RegisterEventType(typeArg);
            else if (definition == typeof(ICommandHandler<>))
                RegisterCommandType(typeArg);
        }
    }
}
