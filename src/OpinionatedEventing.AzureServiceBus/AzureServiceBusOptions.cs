#nullable enable

namespace OpinionatedEventing.AzureServiceBus;

/// <summary>
/// Configuration options for the Azure Service Bus transport.
/// </summary>
public sealed class AzureServiceBusOptions
{
    /// <summary>
    /// Gets or sets the Azure Service Bus connection string.
    /// When set, takes precedence over <see cref="FullyQualifiedNamespace"/>.
    /// Set to <see langword="null"/> to use <see cref="FullyQualifiedNamespace"/> with
    /// <see cref="Azure.Identity.DefaultAzureCredential"/>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the fully qualified namespace (e.g. <c>mybus.servicebus.windows.net</c>).
    /// Used together with <see cref="Azure.Identity.DefaultAzureCredential"/> when
    /// <see cref="ConnectionString"/> is <see langword="null"/>.
    /// </summary>
    public string? FullyQualifiedNamespace { get; set; }

    /// <summary>
    /// Gets or sets the logical name of this consuming service.
    /// Used as the subscription name on event topics.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether topics, subscriptions, and queues should be
    /// created or updated at application startup. Defaults to <see langword="false"/>.
    /// Safe to enable in development; leave disabled in production where infrastructure is
    /// managed externally.
    /// </summary>
    public bool AutoCreateResources { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether commands decorated with
    /// <see cref="OpinionatedEventing.AzureServiceBus.Attributes.SessionEnabledAttribute"/>
    /// use session-enabled queues. Defaults to <see langword="false"/>.
    /// </summary>
    public bool EnableSessions { get; set; }

    /// <summary>
    /// Gets or sets the maximum delivery count before a received message is dead-lettered and
    /// written to the outbox as a failed record. Should match the broker's configured max
    /// delivery count when <see cref="AutoCreateResources"/> is <see langword="false"/>.
    /// Defaults to <c>5</c>.
    /// </summary>
    public int MaxDeliveryCount { get; set; } = 5;

    /// <summary>
    /// Gets or sets the maximum number of concurrent calls to the message handler for regular
    /// (non-session) processors. Defaults to <c>1</c>.
    /// </summary>
    /// <remarks>
    /// A value of <c>1</c> serialises message processing within each processor, making handler
    /// idempotency easier to reason about and preventing database connection spikes. Increase to
    /// a higher value (e.g. <c>16</c>–<c>32</c>) when throughput matters more than ordering and
    /// handlers are already safe to run concurrently. The ideal value depends on handler latency,
    /// downstream connection pool sizes, and Service Bus prefetch settings.
    /// </remarks>
    public int MaxConcurrentCalls { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum number of concurrent sessions processed by session-enabled
    /// processors. Defaults to <c>1</c>.
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 1;
}
