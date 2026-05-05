#nullable enable

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Options;
using OpinionatedEventing.Outbox.Diagnostics;

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
    private readonly IMessageTypeRegistry _registry;
    private readonly IOutboxTransactionGuard? _transactionGuard;
    private readonly IOptions<OpinionatedEventingOptions> _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initialises a new <see cref="OutboxPublisher"/>.</summary>
    public OutboxPublisher(
        IOutboxStore store,
        IMessagingContext messagingContext,
        IMessageTypeRegistry registry,
        IOptions<OpinionatedEventingOptions> options,
        TimeProvider timeProvider,
        IEnumerable<IOutboxTransactionGuard> transactionGuards)
    {
        _store = store;
        _messagingContext = messagingContext;
        _registry = registry;
        _options = options;
        _timeProvider = timeProvider;
        _transactionGuard = transactionGuards.FirstOrDefault();
    }

    /// <inheritdoc/>
    public async Task PublishEventAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        _transactionGuard?.EnsureTransaction();
        var message = CreateMessage(@event, MessageKind.Event);
        var sw = Stopwatch.GetTimestamp();
        using var activity = OutboxDiagnostics.StartPublishActivity(message.MessageType, nameof(MessageKind.Event), message.CorrelationId, message.CausationId);
        try
        {
            await _store.SaveAsync(message, cancellationToken).ConfigureAwait(false);
            OutboxDiagnostics.Pending.Add(1);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            OutboxDiagnostics.PublishDuration.Record(Stopwatch.GetElapsedTime(sw).TotalMilliseconds);
        }
    }

    /// <inheritdoc/>
    public async Task SendCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        _transactionGuard?.EnsureTransaction();
        var message = CreateMessage(command, MessageKind.Command);
        var sw = Stopwatch.GetTimestamp();
        using var activity = OutboxDiagnostics.StartPublishActivity(message.MessageType, nameof(MessageKind.Command), message.CorrelationId, message.CausationId);
        try
        {
            await _store.SaveAsync(message, cancellationToken).ConfigureAwait(false);
            OutboxDiagnostics.Pending.Add(1);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            OutboxDiagnostics.PublishDuration.Record(Stopwatch.GetElapsedTime(sw).TotalMilliseconds);
        }
    }

    private OutboxMessage CreateMessage<T>(T payload, MessageKind kind)
    {
        var runtimeType = payload!.GetType();
        var serializerOptions = _options.Value.SerializerOptions;
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = _registry.GetIdentifier(runtimeType),
            Payload = JsonSerializer.Serialize(payload, runtimeType, serializerOptions),
            MessageKind = kind,
            CorrelationId = _messagingContext.CorrelationId,
            CausationId = _messagingContext.CausationId,
            CreatedAt = _timeProvider.GetUtcNow(),
        };
    }
}
