#nullable enable

namespace OpinionatedEventing.DependencyInjection;

/// <summary>
/// Holds the set of event and command types that have registered handlers.
/// Populated during DI configuration by <see cref="OpinionatedEventingBuilder.AddHandlersFromAssemblies"/>,
/// <c>AddSaga&lt;TOrchestrator&gt;</c>, and <c>AddSagaParticipant&lt;TParticipant&gt;</c>.
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
}
