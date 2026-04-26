namespace OpinionatedEventing.Attributes;

/// <summary>
/// Overrides the default topic name derived from the message type name.
/// Apply to an <see cref="IEvent"/> implementation to control the broker topic
/// used for publishing and subscribing.
/// </summary>
/// <remarks>
/// When not applied the topic name is derived from the message type by convention
/// (e.g. <c>OrderPlaced</c> → <c>order-placed</c>).
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class MessageTopicAttribute : Attribute
{
    /// <summary>Gets the explicit topic name for this message type.</summary>
    public string TopicName { get; }

    /// <summary>Initialises a new <see cref="MessageTopicAttribute"/> with the given topic name.</summary>
    /// <param name="topicName">The broker topic name to use for this message type.</param>
    public MessageTopicAttribute(string topicName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicName);
        TopicName = topicName;
    }
}
