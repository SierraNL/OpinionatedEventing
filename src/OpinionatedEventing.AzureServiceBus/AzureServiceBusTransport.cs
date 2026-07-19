#nullable enable

using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpinionatedEventing.AzureServiceBus.Attributes;
using OpinionatedEventing.AzureServiceBus.Routing;
using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.AzureServiceBus;

/// <summary>
/// Azure Service Bus implementation of <see cref="ITransport"/>.
/// Forwards outbox messages to the correct ASB topic (events) or queue (commands).
/// </summary>
internal sealed class AzureServiceBusTransport : ITransport, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly IMessageTypeRegistry _registry;
    private readonly IServiceBusMessageEnvelope _envelope;
    private readonly IOptions<AzureServiceBusOptions> _options;
    private readonly ILogger<AzureServiceBusTransport> _logger;
    private readonly ConcurrentDictionary<string, Lazy<ServiceBusSender>> _senders = new();

    /// <summary>Initialises a new <see cref="AzureServiceBusTransport"/>.</summary>
    public AzureServiceBusTransport(
        ServiceBusClient client,
        IMessageTypeRegistry registry,
        IServiceBusMessageEnvelope envelope,
        IOptions<AzureServiceBusOptions> options,
        ILogger<AzureServiceBusTransport> logger)
    {
        _client = client;
        _registry = registry;
        _envelope = envelope;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var type = _registry.Resolve(message.MessageType);

        var destination = message.MessageKind == MessageKind.Event
            ? MessageNamingConvention.GetTopicName(type)
            : MessageNamingConvention.GetQueueName(type);

        var sender = _senders.GetOrAdd(destination,
            static (name, client) => new Lazy<ServiceBusSender>(() => client.CreateSender(name)),
            _client).Value;

        var sbMessage = _envelope.CreateMessage(message);

        var opts = _options.Value;
        if (opts.EnableSessions
            && message.MessageKind == MessageKind.Command
            && Attribute.IsDefined(type, typeof(SessionEnabledAttribute)))
        {
            sbMessage.SessionId = message.CorrelationId.ToString();
        }

        await sender.SendMessageAsync(sbMessage, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Forwarded outbox message {MessageId} ({MessageKind}: {MessageType}) to '{Destination}'.",
            message.Id, message.MessageKind, message.MessageType, destination);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var lazy in _senders.Values.Where(l => l.IsValueCreated))
            await lazy.Value.DisposeAsync().ConfigureAwait(false);
    }
}
