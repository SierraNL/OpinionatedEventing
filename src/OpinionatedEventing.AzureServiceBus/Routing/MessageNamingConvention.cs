#nullable enable

using System.Text;
using OpinionatedEventing.Attributes;

namespace OpinionatedEventing.AzureServiceBus.Routing;

/// <summary>
/// Derives Azure Service Bus topic and queue names from CLR message types.
/// </summary>
internal static class MessageNamingConvention
{
    /// <summary>
    /// Returns the topic name for the given event type.
    /// Uses <see cref="MessageTopicAttribute"/> if present; otherwise converts the type name
    /// from PascalCase to kebab-case (e.g. <c>OrderPlaced</c> → <c>order-placed</c>).
    /// </summary>
    internal static string GetTopicName(Type eventType)
    {
        var attr = (MessageTopicAttribute?)Attribute.GetCustomAttribute(
            eventType, typeof(MessageTopicAttribute));
        return attr?.TopicName ?? ToKebabCase(eventType.Name);
    }

    /// <summary>
    /// Returns the queue name for the given command type.
    /// Uses <see cref="MessageQueueAttribute"/> if present; otherwise converts the type name
    /// from PascalCase to kebab-case (e.g. <c>ProcessPayment</c> → <c>process-payment</c>).
    /// </summary>
    internal static string GetQueueName(Type commandType)
    {
        var attr = (MessageQueueAttribute?)Attribute.GetCustomAttribute(
            commandType, typeof(MessageQueueAttribute));
        return attr?.QueueName ?? ToKebabCase(commandType.Name);
    }

    private static string ToKebabCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
                sb.Append('-');
            sb.Append(char.ToLower(name[i]));
        }
        return sb.ToString();
    }
}
