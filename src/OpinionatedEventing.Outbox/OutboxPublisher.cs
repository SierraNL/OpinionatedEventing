#nullable enable

using System.Text.Json;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Options;

namespace OpinionatedEventing.Outbox;

/// <summary>
/// Default <see cref="IPublisher"/> implementation. Serialises messages to JSON and writes them
/// to <see cref="IOutboxStore"/> within the caller's ambient transaction.
/// Broker delivery is performed asynchronously by <see cref="OutboxDispatcherWorker"/>.
/// </summary>
internal sealed class OutboxPublisher : IPublisher
{
    private readonly IOutboxStore _store;
    private readonly IMessagingContext _messagingContext;
    private readonly IOutboxTransactionGuard? _transactionGuard;
    private readonly IOptions<OpinionatedEventingOptions> _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initialises a new <see cref="OutboxPublisher"/>.</summary>
    public OutboxPublisher(
        IOutboxStore store,
        IMessagingContext messagingContext,
        IOptions<OpinionatedEventingOptions> options,
        TimeProvider timeProvider,
        IEnumerable<IOutboxTransactionGuard> transactionGuards)
    {
        _store = store;
        _messagingContext = messagingContext;
        _options = options;
        _timeProvider = timeProvider;
        _transactionGuard = transactionGuards.FirstOrDefault();
    }

    /// <inheritdoc/>
    public Task PublishEventAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        _transactionGuard?.EnsureTransaction();
        return _store.SaveAsync(CreateMessage(@event, "Event"), cancellationToken);
    }

    /// <inheritdoc/>
    public Task SendCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        _transactionGuard?.EnsureTransaction();
        return _store.SaveAsync(CreateMessage(command, "Command"), cancellationToken);
    }

    private OutboxMessage CreateMessage<T>(T payload, string kind)
    {
        var serializerOptions = _options.Value.SerializerOptions;
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            // AssemblyQualifiedName is null only for array types, pointer types, and open
            // generics — none of which can satisfy the IEvent / ICommand constraints here.
            MessageType = typeof(T).AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(payload, serializerOptions),
            MessageKind = kind,
            CorrelationId = _messagingContext.CorrelationId,
            CausationId = _messagingContext.CausationId,
            CreatedAt = _timeProvider.GetUtcNow(),
        };
    }
}
