#nullable enable

namespace OpinionatedEventing.Outbox;

/// <summary>
/// Abstraction over the message broker transport layer.
/// Implemented by transport packages such as <c>OpinionatedEventing.AzureServiceBus</c>
/// and <c>OpinionatedEventing.RabbitMQ</c>.
/// Application code must not call this interface directly — use <see cref="IPublisher"/> instead.
/// </summary>
public interface ITransport
{
    /// <summary>
    /// Forwards an outbox message to the underlying broker.
    /// </summary>
    /// <param name="message">The outbox message to send.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
