#nullable enable

namespace OpinionatedEventing.Sagas;

internal sealed class SagaEventHandlerAdapter<TEvent>(ISagaDispatcher dispatcher)
    : IEventHandler<TEvent>
    where TEvent : class, IEvent
{
    public Task HandleAsync(TEvent @event, CancellationToken cancellationToken)
        => dispatcher.DispatchAsync(@event, cancellationToken);
}
