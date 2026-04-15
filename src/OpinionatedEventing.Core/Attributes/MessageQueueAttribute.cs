namespace OpinionatedEventing.Attributes;

/// <summary>
/// Overrides the default queue name derived from the message type name.
/// Apply to an <see cref="ICommand"/> implementation to control the broker queue
/// used for sending and receiving.
/// </summary>
/// <remarks>
/// When not applied the queue name is derived from the message type by convention
/// (e.g. <c>ProcessPayment</c> → <c>process-payment</c>).
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class MessageQueueAttribute : Attribute
{
    /// <summary>Gets the explicit queue name for this message type.</summary>
    public string QueueName { get; }

    /// <summary>Initialises a new <see cref="MessageQueueAttribute"/> with the given queue name.</summary>
    /// <param name="queueName">The broker queue name to use for this message type.</param>
    public MessageQueueAttribute(string queueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        QueueName = queueName;
    }
}
