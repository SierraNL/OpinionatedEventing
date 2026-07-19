#nullable enable

using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.CloudEvents;

/// <summary>
/// Configuration options for the CloudEvents 1.0 structured envelope mapping.
/// </summary>
public sealed class CloudEventsOptions
{
    /// <summary>
    /// Gets or sets the CloudEvents <c>source</c> attribute identifying the service that produced
    /// the event, e.g. <c>new Uri("urn:order-service")</c>. Must be set before any event is
    /// serialised.
    /// </summary>
    public Uri? Source { get; set; }

    /// <summary>
    /// Gets or sets an optional formatter for the CloudEvents <c>type</c> attribute. When
    /// <see langword="null"/> (the default), <see cref="OutboxMessage.MessageType"/> is used
    /// as-is. Set this to produce a different convention, e.g. a reverse-DNS style type such as
    /// <c>com.company.orderservice.orderplaced</c> for consumers like Azure Event Grid or Dapr
    /// that route on <c>type</c>.
    /// </summary>
    public Func<OutboxMessage, string>? TypeFormatter { get; set; }
}
