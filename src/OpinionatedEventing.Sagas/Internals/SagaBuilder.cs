namespace OpinionatedEventing.Sagas;

internal sealed class SagaBuilder<TSagaState> : ISagaBuilder<TSagaState>
    where TSagaState : class, new()
{
    private readonly SagaDefinition<TSagaState> _def = new();

    public ISagaBuilder<TSagaState> StartWith<TEvent>(
        Func<TEvent, TSagaState, ISagaContext, Task> handler)
        where TEvent : IEvent
    {
        _def.StartEventType = typeof(TEvent);
        _def.EventHandlers[typeof(TEvent)] = (e, s, c) => handler((TEvent)e, s, c);
        return this;
    }

    public ISagaBuilder<TSagaState> Then<TEvent>(
        Func<TEvent, TSagaState, ISagaContext, Task> handler)
        where TEvent : IEvent
    {
        _def.EventHandlers[typeof(TEvent)] = (e, s, c) => handler((TEvent)e, s, c);
        return this;
    }

    public ISagaBuilder<TSagaState> CompensateWith<TEvent>(
        Func<TEvent, TSagaState, ISagaContext, Task> handler)
        where TEvent : IEvent
    {
        Func<object, TSagaState, ISagaContext, Task> wrapped = (e, s, c) => handler((TEvent)e, s, c);
        _def.CompensationHandlers.Add((typeof(TEvent), wrapped));
        _def.CompensationHandlerByType[typeof(TEvent)] = wrapped;
        return this;
    }

    public ISagaBuilder<TSagaState> OnTimeout(Func<TSagaState, ISagaContext, Task> handler)
    {
        _def.TimeoutHandler = handler;
        return this;
    }

    public ISagaBuilder<TSagaState> ExpireAfter(TimeSpan duration)
    {
        _def.ExpiresAfter = duration;
        return this;
    }

    public ISagaBuilder<TSagaState> ExpireAt(DateTimeOffset timestamp)
    {
        _def.ExpiresAt = timestamp;
        return this;
    }

    public ISagaBuilder<TSagaState> CorrelateBy<TEvent>(Func<TEvent, string> expression)
        where TEvent : IEvent
    {
        _def.CorrelationExpressions[typeof(TEvent)] = e => expression((TEvent)e);
        return this;
    }

    public SagaDefinition<TSagaState> Build() => _def;
}
