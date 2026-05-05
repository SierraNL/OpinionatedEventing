#nullable enable

using System.Text;
using OpinionatedEventing.Attributes;

namespace OpinionatedEventing.RabbitMQ.Routing;

/// <summary>
/// Derives RabbitMQ exchange and queue names from CLR message types.
/// </summary>
internal static class MessageNamingConvention
{
    /// <summary>
    /// Returns the exchange name for the given event type.
    /// Uses <see cref="MessageTopicAttribute"/> if present; otherwise converts the type name
    /// from PascalCase to kebab-case (e.g. <c>OrderPlaced</c> → <c>order-placed</c>).
    /// </summary>
    internal static string GetExchangeName(Type eventType)
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

    /// <summary>
    /// Returns the event consumer queue name for the given event type and service name.
    /// Format: <c>{serviceName}.{event-name}</c>.
    /// </summary>
    internal static string GetEventQueueName(Type eventType, string serviceName)
        => $"{serviceName}.{GetExchangeName(eventType)}";

    private static string ToKebabCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                var prev = name[i - 1];
                var next = i + 1 < name.Length ? name[i + 1] : '\0';
                // Insert hyphen at a word boundary: after lowercase, or before the last uppercase of a run
                // e.g. HTTPRequest → http-request, OrderPlaced → order-placed
                if (char.IsLower(prev) || (char.IsUpper(prev) && char.IsLower(next)))
                    sb.Append('-');
            }
            sb.Append(char.ToLower(c));
        }
        return sb.ToString();
    }
}
