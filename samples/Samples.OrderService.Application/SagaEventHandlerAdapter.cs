using OpinionatedEventing.Sagas;

namespace Samples.OrderService.Application;

// Bridges IEventHandler<T> (used by the transport consumer and topology initializer)
// to ISagaDispatcher. Register one adapter per event type that the saga handles.
// Without this, the RabbitMQ consumer would not create subscriptions for saga events.
public sealed class SagaEventHandlerAdapter<TEvent>(ISagaDispatcher dispatcher)
    : IEventHandler<TEvent>
    where TEvent : class, IEvent
{
    public Task HandleAsync(TEvent @event, CancellationToken cancellationToken)
        => dispatcher.DispatchAsync(@event, cancellationToken);
}
