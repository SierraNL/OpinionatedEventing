namespace OpinionatedEventing.Sagas;

/// <summary>
/// Fluent builder for configuring a <see cref="SagaOrchestrator{TSagaState}"/>.
/// Call inside <c>Configure</c> to register event handlers, timeouts, and compensation steps.
/// </summary>
/// <typeparam name="TSagaState">The saga's durable state type.</typeparam>
public interface ISagaBuilder<TSagaState>
{
    /// <summary>
    /// Registers the event that creates a new saga instance.
    /// Exactly one <c>StartWith</c> call is required per saga.
    /// </summary>
    ISagaBuilder<TSagaState> StartWith<TEvent>(
        Func<TEvent, TSagaState, ISagaContext, Task> handler)
        where TEvent : IEvent;

    /// <summary>Registers a handler for a subsequent event in the saga flow.</summary>
    ISagaBuilder<TSagaState> Then<TEvent>(
        Func<TEvent, TSagaState, ISagaContext, Task> handler)
        where TEvent : IEvent;

    /// <summary>
    /// Registers a compensation handler invoked when <typeparamref name="TEvent"/> arrives
    /// while the saga is <see cref="SagaStatus.Active"/>.
    /// The saga transitions to <see cref="SagaStatus.Compensating"/> before the handler runs.
    /// </summary>
    ISagaBuilder<TSagaState> CompensateWith<TEvent>(
        Func<TEvent, TSagaState, ISagaContext, Task> handler)
        where TEvent : IEvent;

    /// <summary>Registers the handler invoked when the saga exceeds its configured expiry.</summary>
    ISagaBuilder<TSagaState> OnTimeout(Func<TSagaState, ISagaContext, Task> handler);

    /// <summary>Configures the saga to expire after the given <paramref name="duration"/> from start.</summary>
    ISagaBuilder<TSagaState> ExpireAfter(TimeSpan duration);

    /// <summary>Configures the saga to expire at an absolute point in time.</summary>
    ISagaBuilder<TSagaState> ExpireAt(DateTimeOffset timestamp);

    /// <summary>
    /// Overrides the default correlation strategy for <typeparamref name="TEvent"/>.
    /// By default, the <c>CorrelationId</c> property on the message is used.
    /// </summary>
    ISagaBuilder<TSagaState> CorrelateBy<TEvent>(Func<TEvent, string> expression)
        where TEvent : IEvent;
}
