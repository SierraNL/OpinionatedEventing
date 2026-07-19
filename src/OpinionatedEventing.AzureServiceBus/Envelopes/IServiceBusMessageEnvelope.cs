#nullable enable

using Azure.Messaging.ServiceBus;
using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.AzureServiceBus;

/// <summary>
/// Builds and parses the Azure Service Bus wire representation of an outbox message. Registered as
/// a singleton; <see cref="DefaultServiceBusMessageEnvelope"/> is the built-in broker-native
/// behaviour. Replace the registration (e.g. via <c>UseCloudEventsEnvelope()</c>) to change how
/// messages are represented on the wire without touching <see cref="AzureServiceBusTransport"/> or
/// <see cref="AzureServiceBusConsumerWorker"/>.
/// </summary>
public interface IServiceBusMessageEnvelope
{
    /// <summary>
    /// Builds the outbound <see cref="ServiceBusMessage"/> for <paramref name="message"/>.
    /// </summary>
    ServiceBusMessage CreateMessage(OutboxMessage message);

    /// <summary>
    /// Parses an inbound <see cref="ServiceBusReceivedMessage"/> back into a <see cref="ParsedEnvelope"/>.
    /// </summary>
    /// <exception cref="MessageEnvelopeFormatException">
    /// The message is missing required data or is otherwise malformed.
    /// </exception>
    ParsedEnvelope Parse(ServiceBusReceivedMessage message);
}
