#nullable enable

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpinionatedEventing.DependencyInjection;
using OpinionatedEventing.RabbitMQ.Routing;
using RabbitMQ.Client;

namespace OpinionatedEventing.RabbitMQ;

/// <summary>
/// Hosted service that idempotently declares RabbitMQ exchanges, queues, and bindings at
/// application startup when <see cref="RabbitMQOptions.AutoDeclareTopology"/> is
/// <see langword="true"/>.
/// </summary>
internal sealed class RabbitMQTopologyInitializer : IHostedService
{
    private readonly IConnection _connection;
    private readonly MessageHandlerRegistry _registry;
    private readonly IOptions<RabbitMQOptions> _options;
    private readonly ILogger<RabbitMQTopologyInitializer> _logger;

    /// <summary>Initialises a new <see cref="RabbitMQTopologyInitializer"/>.</summary>
    public RabbitMQTopologyInitializer(
        IConnection connection,
        MessageHandlerRegistry registry,
        IOptions<RabbitMQOptions> options,
        ILogger<RabbitMQTopologyInitializer> logger)
    {
        _connection = connection;
        _registry = registry;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        if (!opts.AutoDeclareTopology)
            return;

        var eventTypes = _registry.EventTypes;
        var commandTypes = _registry.CommandTypes;

        await using var channel = await _connection
            .CreateChannelAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var eventType in eventTypes)
        {
            var exchangeName = MessageNamingConvention.GetExchangeName(eventType);
            await DeclareEventTopologyAsync(channel, exchangeName, opts.ServiceName, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var commandType in commandTypes)
        {
            var queueName = MessageNamingConvention.GetQueueName(commandType);
            await DeclareCommandTopologyAsync(channel, queueName, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task DeclareEventTopologyAsync(
        IChannel channel,
        string exchangeName,
        string serviceName,
        CancellationToken ct)
    {
        try
        {
            await channel.ExchangeDeclareAsync(
                exchange: exchangeName,
                type: ExchangeType.Fanout,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct).ConfigureAwait(false);

            _logger.LogDebug("Declared fanout exchange '{Exchange}'.", exchangeName);

            if (string.IsNullOrEmpty(serviceName))
            {
                _logger.LogWarning(
                    "ServiceName is not configured — skipping queue declaration for exchange '{Exchange}'.",
                    exchangeName);
                return;
            }

            var queueName = $"{serviceName}.{exchangeName}";
            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct).ConfigureAwait(false);

            await channel.QueueBindAsync(
                queue: queueName,
                exchange: exchangeName,
                routingKey: string.Empty,
                arguments: null,
                cancellationToken: ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Declared queue '{Queue}' bound to exchange '{Exchange}'.",
                queueName, exchangeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to declare event topology for exchange '{Exchange}'.", exchangeName);
            throw;
        }
    }

    private async Task DeclareCommandTopologyAsync(
        IChannel channel,
        string queueName,
        CancellationToken ct)
    {
        try
        {
            await channel.ExchangeDeclareAsync(
                exchange: queueName,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct).ConfigureAwait(false);

            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct).ConfigureAwait(false);

            await channel.QueueBindAsync(
                queue: queueName,
                exchange: queueName,
                routingKey: queueName,
                arguments: null,
                cancellationToken: ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Declared command queue '{Queue}' bound to direct exchange '{Exchange}'.",
                queueName, queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to declare command topology for queue '{Queue}'.", queueName);
            throw;
        }
    }

}
