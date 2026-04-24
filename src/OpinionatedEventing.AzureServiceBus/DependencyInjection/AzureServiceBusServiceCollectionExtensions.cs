#nullable enable

using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpinionatedEventing;
using OpinionatedEventing.AzureServiceBus;
using OpinionatedEventing.AzureServiceBus.DependencyInjection;
using OpinionatedEventing.Outbox;

// Placed in this namespace so the extension is available without an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering the Azure Service Bus transport.
/// </summary>
public static class AzureServiceBusServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Azure Service Bus transport. Requires a prior call to
    /// <c>AddOpinionatedEventing()</c> and a registered <see cref="IOutboxStore"/>.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Delegate to configure <see cref="AzureServiceBusOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddAzureServiceBusTransport(
        this IServiceCollection services,
        Action<AzureServiceBusOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        services.TryAddSingleton(TimeProvider.System);

        // Capture the service collection for handler-type scanning at host startup.
        services.TryAddSingleton(new ServiceCollectionAccessor(services));

        services.TryAddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureServiceBusOptions>>().Value;
            return BuildServiceBusClient(opts);
        });

        services.TryAddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureServiceBusOptions>>().Value;
            return BuildAdministrationClient(opts);
        });

        services.TryAddSingleton<IConsumerPauseController, NullConsumerPauseController>();
        services.TryAddSingleton<ITransport, AzureServiceBusTransport>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, TopologyInitializer>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AzureServiceBusConsumerWorker>());

        return services;
    }

    private static ServiceBusClient BuildServiceBusClient(AzureServiceBusOptions opts)
    {
        if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
            return new ServiceBusClient(opts.ConnectionString);

        if (!string.IsNullOrWhiteSpace(opts.FullyQualifiedNamespace))
            return new ServiceBusClient(opts.FullyQualifiedNamespace, new DefaultAzureCredential());

        throw new InvalidOperationException(
            "AzureServiceBusOptions requires either ConnectionString or FullyQualifiedNamespace to be set.");
    }

    private static ServiceBusAdministrationClient BuildAdministrationClient(AzureServiceBusOptions opts)
    {
        if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
            return new ServiceBusAdministrationClient(opts.ConnectionString);

        if (!string.IsNullOrWhiteSpace(opts.FullyQualifiedNamespace))
            return new ServiceBusAdministrationClient(opts.FullyQualifiedNamespace, new DefaultAzureCredential());

        throw new InvalidOperationException(
            "AzureServiceBusOptions requires either ConnectionString or FullyQualifiedNamespace to be set.");
    }
}
