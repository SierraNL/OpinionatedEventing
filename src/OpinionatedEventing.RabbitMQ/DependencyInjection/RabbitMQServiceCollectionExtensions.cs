#nullable enable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpinionatedEventing;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.RabbitMQ;
using OpinionatedEventing.RabbitMQ.DependencyInjection;
using RabbitMQ.Client;

// Placed in this namespace so the extension is available without an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering the RabbitMQ transport.
/// </summary>
public static class RabbitMQServiceCollectionExtensions
{
    /// <summary>
    /// Registers the RabbitMQ transport. Requires a prior call to
    /// <c>AddOpinionatedEventing()</c> and a registered <see cref="IOutboxStore"/>.
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

        // Capture the service collection for handler-type scanning at host startup.
        services.TryAddSingleton(new ServiceCollectionAccessor(services));

        services.TryAddSingleton<IConnection>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RabbitMQOptions>>().Value;
            var config = sp.GetService<IConfiguration>();
            var connectionString = ResolveConnectionString(opts, config);
            var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
            return factory.CreateConnectionAsync().GetAwaiter().GetResult();
        });

        services.TryAddSingleton<IConsumerPauseController, NullConsumerPauseController>();
        services.TryAddSingleton<ITransport, RabbitMQTransport>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, RabbitMQTopologyInitializer>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, RabbitMQConsumerWorker>());

        return services;
    }

    private static string ResolveConnectionString(RabbitMQOptions opts, IConfiguration? config)
    {
        if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
            return opts.ConnectionString;

        var aspireConnectionString = config?["ConnectionStrings:rabbitmq"];
        if (!string.IsNullOrWhiteSpace(aspireConnectionString))
            return aspireConnectionString;

        throw new InvalidOperationException(
            "RabbitMQOptions requires either ConnectionString to be set, " +
            "or a 'ConnectionStrings:rabbitmq' entry in IConfiguration (Aspire service discovery).");
    }
}
