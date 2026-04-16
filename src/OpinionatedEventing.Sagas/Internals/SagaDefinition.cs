namespace OpinionatedEventing.Sagas;

internal sealed class SagaDefinition<TSagaState> where TSagaState : class, new()
{
    // The event type registered via StartWith<T>
    public Type? StartEventType { get; set; }

    // Normal handlers: StartWith + Then
    public Dictionary<Type, Func<object, TSagaState, ISagaContext, Task>> EventHandlers { get; } = new();

    // Compensation handlers in registration order (for future cascade support)
    public List<(Type EventType, Func<object, TSagaState, ISagaContext, Task> Handler)> CompensationHandlers { get; } = new();

    // Fast lookup by event type
    public Dictionary<Type, Func<object, TSagaState, ISagaContext, Task>> CompensationHandlerByType { get; } = new();

    // Custom correlation expressions: event type → string key extractor
    public Dictionary<Type, Func<object, string>> CorrelationExpressions { get; } = new();

    public TimeSpan? ExpiresAfter { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Func<TSagaState, ISagaContext, Task>? TimeoutHandler { get; set; }
}
