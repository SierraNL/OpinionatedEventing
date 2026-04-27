#nullable enable

using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpinionatedEventing.AzureServiceBus.Attributes;
using OpinionatedEventing.AzureServiceBus.Routing;
using OpinionatedEventing.DependencyInjection;

namespace OpinionatedEventing.AzureServiceBus;

/// <summary>
/// Hosted service that idempotently creates or updates Azure Service Bus topics, subscriptions,
/// and queues at application startup when
/// <see cref="AzureServiceBusOptions.AutoCreateResources"/> is <see langword="true"/>.
/// </summary>
internal sealed class TopologyInitializer : IHostedService
{
    private readonly ServiceBusAdministrationClient _adminClient;
    private readonly MessageHandlerRegistry _registry;
    private readonly IOptions<AzureServiceBusOptions> _options;
    private readonly ILogger<TopologyInitializer> _logger;

    /// <summary>Initialises a new <see cref="TopologyInitializer"/>.</summary>
    public TopologyInitializer(
        ServiceBusAdministrationClient adminClient,
        MessageHandlerRegistry registry,
        IOptions<AzureServiceBusOptions> options,
        ILogger<TopologyInitializer> logger)
    {
        _adminClient = adminClient;
        _registry = registry;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        if (!opts.AutoCreateResources)
            return;

        var eventTypes = _registry.EventTypes;
        var commandTypes = _registry.CommandTypes;

        foreach (var eventType in eventTypes)
        {
            var topicName = MessageNamingConvention.GetTopicName(eventType);
            await EnsureTopicExistsAsync(topicName, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(opts.ServiceName))
                await EnsureSubscriptionExistsAsync(topicName, opts.ServiceName, cancellationToken)
                    .ConfigureAwait(false);
        }

        foreach (var commandType in commandTypes)
        {
            var queueName = MessageNamingConvention.GetQueueName(commandType);
            var requiresSession = opts.EnableSessions
                && Attribute.IsDefined(commandType, typeof(SessionEnabledAttribute));
            await EnsureQueueExistsAsync(queueName, requiresSession, opts.MaxDeliveryCount, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureTopicExistsAsync(string topicName, CancellationToken ct)
    {
        try
        {
            if (await _adminClient.TopicExistsAsync(topicName, ct).ConfigureAwait(false))
            {
                _logger.LogDebug("Topic '{Topic}' already exists.", topicName);
                return;
            }

            await _adminClient.CreateTopicAsync(topicName, ct).ConfigureAwait(false);
            _logger.LogInformation("Created topic '{Topic}'.", topicName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure topic '{Topic}' exists.", topicName);
            throw;
        }
    }

    private async Task EnsureSubscriptionExistsAsync(string topicName, string subscriptionName, CancellationToken ct)
    {
        try
        {
            if (await _adminClient.SubscriptionExistsAsync(topicName, subscriptionName, ct).ConfigureAwait(false))
            {
                _logger.LogDebug("Subscription '{Subscription}' on topic '{Topic}' already exists.",
                    subscriptionName, topicName);
                return;
            }

            await _adminClient.CreateSubscriptionAsync(topicName, subscriptionName, ct).ConfigureAwait(false);
            _logger.LogInformation("Created subscription '{Subscription}' on topic '{Topic}'.",
                subscriptionName, topicName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure subscription '{Subscription}' on topic '{Topic}' exists.",
                subscriptionName, topicName);
            throw;
        }
    }

    private async Task EnsureQueueExistsAsync(
        string queueName,
        bool requiresSession,
        int maxDeliveryCount,
        CancellationToken ct)
    {
        try
        {
            if (await _adminClient.QueueExistsAsync(queueName, ct).ConfigureAwait(false))
            {
                var existing = await _adminClient.GetQueueAsync(queueName, ct).ConfigureAwait(false);
                if (existing.Value.MaxDeliveryCount != maxDeliveryCount)
                    _logger.LogWarning(
                        "Queue '{Queue}' already exists with MaxDeliveryCount={Existing} " +
                        "but options specify {Configured}. Update the queue manually or enable AutoCreateResources on a fresh namespace.",
                        queueName, existing.Value.MaxDeliveryCount, maxDeliveryCount);
                else
                    _logger.LogDebug("Queue '{Queue}' already exists.", queueName);
                return;
            }

            var options = new CreateQueueOptions(queueName)
            {
                RequiresSession = requiresSession,
                MaxDeliveryCount = maxDeliveryCount,
            };
            await _adminClient.CreateQueueAsync(options, ct).ConfigureAwait(false);
            _logger.LogInformation("Created queue '{Queue}' (session={RequiresSession}).",
                queueName, requiresSession);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure queue '{Queue}' exists.", queueName);
            throw;
        }
    }

}
