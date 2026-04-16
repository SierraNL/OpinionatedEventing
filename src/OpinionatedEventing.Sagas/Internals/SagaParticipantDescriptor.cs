using Microsoft.Extensions.DependencyInjection;

namespace OpinionatedEventing.Sagas;

internal abstract class SagaParticipantDescriptor
{
    public abstract Task HandleAsync(
        object @event,
        IServiceProvider sp,
        IPublisher publisher,
        CancellationToken ct);
}

internal sealed class SagaParticipantDescriptor<TParticipant, TEvent> : SagaParticipantDescriptor
    where TParticipant : class, ISagaParticipant<TEvent>
    where TEvent : IEvent
{
    public override async Task HandleAsync(
        object @event,
        IServiceProvider sp,
        IPublisher publisher,
        CancellationToken ct)
    {
        if (@event is not TEvent typedEvent) return;

        var participant = sp.GetRequiredService<TParticipant>();

        var prop = typeof(TEvent).GetProperty("CorrelationId");
        _ = Guid.TryParse(prop?.GetValue(typedEvent)?.ToString(), out var corrGuid);

        var context = new SagaContext(corrGuid, publisher, ct);
        await participant.HandleAsync(typedEvent, context, ct);
    }
}
