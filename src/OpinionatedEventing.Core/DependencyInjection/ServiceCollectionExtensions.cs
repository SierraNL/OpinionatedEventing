using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpinionatedEventing;
using OpinionatedEventing.DependencyInjection;
using OpinionatedEventing.Options;

// Placing in this namespace so the extension is available without an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering OpinionatedEventing core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OpinionatedEventing core services and configures options.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Optional delegate to configure <see cref="OpinionatedEventingOptions"/>.</param>
    /// <returns>An <see cref="OpinionatedEventingBuilder"/> for further configuration.</returns>
    public static OpinionatedEventingBuilder AddOpinionatedEventing(
        this IServiceCollection services,
        Action<OpinionatedEventingOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<OpinionatedEventingOptions>();

        services.TryAddScoped<MessagingContext>();
        services.TryAddScoped<IMessagingContext>(sp => sp.GetRequiredService<MessagingContext>());
        services.TryAddSingleton<IMessageHandlerRunner, MessageHandlerRunner>();

        return new OpinionatedEventingBuilder(services);
    }
}
