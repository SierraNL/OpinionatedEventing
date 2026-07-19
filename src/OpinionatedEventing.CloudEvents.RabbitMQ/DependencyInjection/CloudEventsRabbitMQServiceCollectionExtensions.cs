#nullable enable

using Microsoft.Extensions.DependencyInjection.Extensions;
using OpinionatedEventing.CloudEvents;
using OpinionatedEventing.CloudEvents.RabbitMQ;
using OpinionatedEventing.RabbitMQ;

// Placed in this namespace so the extension is available without an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for opting the RabbitMQ transport into the
/// CloudEvents 1.0 structured envelope for events.
/// </summary>
public static class CloudEventsRabbitMQServiceCollectionExtensions
{
    /// <summary>
    /// Wraps events in a CloudEvents 1.0 structured envelope. Commands are unaffected. Requires a
    /// prior call to <c>AddRabbitMQTransport(...)</c>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Delegate to configure <see cref="CloudEventsOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection UseCloudEventsEnvelope(
        this IServiceCollection services,
        Action<CloudEventsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.TryAddSingleton<DefaultRabbitMQMessageEnvelope>();
        services.Replace(
            ServiceDescriptor.Singleton<IRabbitMQMessageEnvelope, CloudEventsRabbitMQMessageEnvelope>());

        return services;
    }
}
