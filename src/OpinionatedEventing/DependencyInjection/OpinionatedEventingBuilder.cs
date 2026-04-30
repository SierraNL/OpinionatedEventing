using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace OpinionatedEventing.DependencyInjection;

/// <summary>
/// Builder returned from <see cref="ServiceCollectionExtensions.AddOpinionatedEventing"/>
/// for further configuration such as registering handlers from specific assemblies.
/// </summary>
public sealed class OpinionatedEventingBuilder
{
    private readonly IServiceCollection _services;
    private readonly MessageTypeRegistry _registry;

    /// <summary>Gets the underlying <see cref="IServiceCollection"/>.</summary>
    public IServiceCollection Services => _services;

    internal OpinionatedEventingBuilder(IServiceCollection services, MessageTypeRegistry registry)
    {
        _services = services;
        _registry = registry;
    }

    /// <summary>
    /// Scans the given <paramref name="assemblies"/> for <see cref="IEventHandler{TEvent}"/>
    /// and <see cref="ICommandHandler{TCommand}"/> implementations and registers them in DI.
    /// The corresponding message types are also registered in <see cref="IMessageTypeRegistry"/>
    /// so their stable identifiers are available at publish and consume time.
    /// Multiple event handlers for the same event type are allowed.
    /// Duplicate command handlers for the same command type throw <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>The same builder instance for chaining.</returns>
    public OpinionatedEventingBuilder AddHandlersFromAssemblies(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract)
                    continue;

                foreach (var iface in type.GetInterfaces())
                {
                    if (!iface.IsGenericType)
                        continue;

                    var definition = iface.GetGenericTypeDefinition();

                    if (definition == typeof(IEventHandler<>))
                    {
                        var alreadyRegistered = _services.Any(
                            d => d.ServiceType == iface && d.ImplementationType == type);
                        if (!alreadyRegistered)
                            _services.AddScoped(iface, type);

                        // Register the event type in the message-type registry.
                        _registry.Register(iface.GetGenericArguments()[0]);
                    }
                    else if (definition == typeof(ICommandHandler<>))
                    {
                        var existing = _services.FirstOrDefault(d => d.ServiceType == iface);
                        if (existing is not null)
                        {
                            // Idempotent: same assembly scanned twice — skip silently.
                            if (existing.ImplementationType == type)
                                continue;

                            var commandType = iface.GetGenericArguments()[0];
                            throw new InvalidOperationException(
                                $"Duplicate ICommandHandler registration for '{commandType.FullName}'. " +
                                $"Existing: '{existing.ImplementationType?.FullName}', " +
                                $"New: '{type.FullName}'. " +
                                "Exactly one command handler per command type is allowed.");
                        }

                        _services.AddScoped(iface, type);

                        // Register the command type in the message-type registry.
                        _registry.Register(iface.GetGenericArguments()[0]);
                    }
                }
            }
        }

        return this;
    }
}
