#nullable enable

using Aspire.Hosting.ApplicationModel;

// Placed in this namespace so the extension is available without an extra using directive.
namespace Aspire.Hosting;

/// <summary>
/// Extension methods on <see cref="IDistributedApplicationBuilder"/> for adding a RabbitMQ
/// messaging resource to an Aspire AppHost.
/// </summary>
public static class RabbitMqMessagingExtensions
{
    /// <summary>
    /// Adds a RabbitMQ server resource with the management plugin enabled.
    /// Referenced projects receive a <c>ConnectionStrings__<paramref name="name"/></c> environment
    /// variable that <c>OpinionatedEventing.RabbitMQ</c> reads automatically via Aspire service discovery.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name; also used as the connection string key.</param>
    /// <returns>A resource builder for the RabbitMQ server resource.</returns>
    public static IResourceBuilder<RabbitMQServerResource> AddRabbitMqMessaging(
        this IDistributedApplicationBuilder builder,
        string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return builder.AddRabbitMQ(name).WithManagementPlugin();
    }
}
