#nullable enable

namespace OpinionatedEventing.RabbitMQ;

/// <summary>
/// Configuration options for the RabbitMQ transport.
/// </summary>
public sealed class RabbitMQOptions
{
    /// <summary>
    /// Gets or sets the AMQP connection string (e.g. <c>amqp://guest:guest@localhost:5672/</c>).
    /// When set, takes precedence over Aspire service discovery.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the logical name of this consuming service.
    /// Used to derive event queue names: <c>{ServiceName}.{event-name}</c>.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether exchanges, queues, and bindings should be declared
    /// idempotently at application startup. Defaults to <see langword="true"/>.
    /// Safe to enable in development; disable in production where topology is managed externally.
    /// </summary>
    public bool AutoDeclareTopology { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of messages to prefetch per consumer channel. Defaults to <c>10</c>.
    /// </summary>
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>
    /// Gets or sets the <c>ConnectionStrings</c> key used for Aspire service-discovery
    /// (e.g. the resource name passed to <c>AddRabbitMqMessaging(name)</c>).
    /// Defaults to <c>"rabbitmq"</c>. Only consulted when <see cref="ConnectionString"/> is not set.
    /// </summary>
    public string AspireConnectionStringName { get; set; } = "rabbitmq";
}
