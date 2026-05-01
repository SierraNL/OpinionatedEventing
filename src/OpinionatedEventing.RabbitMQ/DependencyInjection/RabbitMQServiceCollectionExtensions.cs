#nullable enable

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpinionatedEventing;
using OpinionatedEventing.DependencyInjection;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.RabbitMQ;

// Placed in this namespace so the extension is available without an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering the RabbitMQ transport.
/// </summary>
public static class RabbitMQServiceCollectionExtensions
{
    /// <summary>
    /// Registers the RabbitMQ transport. Requires a prior call to
    /// <c>AddOpinionatedEventing()</c>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Delegate to configure <see cref="RabbitMQOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddRabbitMQTransport(
        this IServiceCollection services,
        Action<RabbitMQOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<RabbitMqConnectionHolder>();
        services.TryAddSingleton<IConsumerPauseController, NullConsumerPauseController>();
        services.TryAddSingleton<ITransport, RabbitMQTransport>();

        // Connection initializer must be registered before topology and consumer so that
        // StartAsync runs first and the holder is populated before the others await it.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, RabbitMqConnectionInitializer>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, RabbitMQTopologyInitializer>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, RabbitMQConsumerWorker>());

        return services;
    }
}
