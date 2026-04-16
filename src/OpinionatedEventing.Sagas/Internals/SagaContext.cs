namespace OpinionatedEventing.Sagas;

internal sealed class SagaContext : ISagaContext
{
    private readonly IPublisher _publisher;
    private readonly CancellationToken _cancellationToken;

    internal bool IsCompleted { get; private set; }

    public SagaContext(Guid correlationId, IPublisher publisher, CancellationToken cancellationToken)
    {
        CorrelationId = correlationId;
        _publisher = publisher;
        _cancellationToken = cancellationToken;
    }

    public Guid CorrelationId { get; }

    public Task SendCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand
        => _publisher.SendCommandAsync(command,
            cancellationToken == default ? _cancellationToken : cancellationToken);

    public Task PublishEventAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
        => _publisher.PublishEventAsync(@event,
            cancellationToken == default ? _cancellationToken : cancellationToken);

    public void Complete() => IsCompleted = true;
}
