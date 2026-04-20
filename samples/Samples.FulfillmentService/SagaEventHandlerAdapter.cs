using OpinionatedEventing.Sagas;

namespace Samples.FulfillmentService;

// Bridges IEventHandler<T> (transport subscription mechanism) to ISagaDispatcher
// so the RabbitMQ consumer routes PaymentReceived to FulfillmentParticipant.
internal sealed class SagaEventHandlerAdapter<TEvent>(ISagaDispatcher dispatcher)
    : IEventHandler<TEvent>
    where TEvent : class, IEvent
{
    public Task HandleAsync(TEvent @event, CancellationToken cancellationToken)
        => dispatcher.DispatchAsync(@event, cancellationToken);
}
