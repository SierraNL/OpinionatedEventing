using System.Text.Json;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Sagas.Options;

namespace OpinionatedEventing.Sagas;

internal sealed class SagaDispatcher : ISagaDispatcher
{
    private readonly IServiceProvider _sp;
    private readonly IEnumerable<SagaDescriptor> _sagaDescriptors;
    private readonly IEnumerable<SagaParticipantDescriptor> _participantDescriptors;
    private readonly ISagaStateStore _stateStore;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions? _serializerOptions;

    public SagaDispatcher(
        IServiceProvider sp,
        IEnumerable<SagaDescriptor> sagaDescriptors,
        IEnumerable<SagaParticipantDescriptor> participantDescriptors,
        ISagaStateStore stateStore,
        IPublisher publisher,
        TimeProvider timeProvider,
        IOptions<SagaOptions> options)
    {
        _sp = sp;
        _sagaDescriptors = sagaDescriptors;
        _participantDescriptors = participantDescriptors;
        _stateStore = stateStore;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _serializerOptions = options.Value.SerializerOptions;
    }

    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        foreach (var descriptor in _sagaDescriptors)
            await descriptor.HandleEventAsync(
                // TEvent : IEvent does not imply notnull without an explicit class/notnull constraint;
                // the ! is safe because callers always pass a non-null event instance.
                @event!, _sp, _stateStore, _publisher, _timeProvider, _serializerOptions, cancellationToken);

        foreach (var descriptor in _participantDescriptors)
            // Same reasoning — TEvent : IEvent does not imply notnull; caller guarantees non-null.
            await descriptor.HandleAsync(@event!, _sp, _publisher, cancellationToken);
    }
}
