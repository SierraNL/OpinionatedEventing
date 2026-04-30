using Microsoft.Extensions.DependencyInjection.Extensions;
using OpinionatedEventing.DependencyInjection;
using OpinionatedEventing.Sagas;
using OpinionatedEventing.Sagas.Options;

// Placing in this namespace so the extension is available without an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering the saga engine.
/// </summary>
public static class SagaDependencyInjectionExtensions
{
    /// <summary>
    /// Registers the saga engine, timeout worker, and options.
    /// Call <see cref="AddSaga{TOrchestrator}"/> and <see cref="AddSagaParticipant{TParticipant}"/>
    /// to register individual orchestrators and participants.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional delegate to configure <see cref="SagaOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddOpinionatedEventingSagas(
        this IServiceCollection services,
        Action<SagaOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<SagaOptions>();

        services.TryAddScoped<ISagaDispatcher, SagaDispatcher>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<SagaTimeoutWorker>();

        return services;
    }

    /// <summary>
    /// Registers a saga orchestrator and its event descriptors.
    /// <typeparamref name="TOrchestrator"/> must inherit from
    /// <see cref="SagaOrchestrator{TSagaState}"/>.
    /// </summary>
    /// <typeparam name="TOrchestrator">The orchestrator type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSaga<TOrchestrator>(this IServiceCollection services)
        where TOrchestrator : class
    {
        var stateType = FindSagaStateType(typeof(TOrchestrator))
            ?? throw new InvalidOperationException(
                $"'{typeof(TOrchestrator).Name}' must inherit from SagaOrchestrator<TSagaState>.");

        services.TryAddTransient(typeof(TOrchestrator));

        var descriptorType = typeof(SagaDescriptor<,>).MakeGenericType(typeof(TOrchestrator), stateType);
        var instance = (SagaDescriptor)Activator.CreateInstance(descriptorType)!;
        services.AddSingleton(instance);

        var registry = GetRegistry(services);
        if (registry is not null)
        {
            foreach (var eventType in instance.GetHandledEventTypes())
                registry.RegisterEventType(eventType);
        }

        return services;
    }

    /// <summary>
    /// Registers a choreography participant.
    /// <typeparamref name="TParticipant"/> must implement <see cref="ISagaParticipant{TEvent}"/>.
    /// </summary>
    /// <typeparam name="TParticipant">The participant type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSagaParticipant<TParticipant>(this IServiceCollection services)
        where TParticipant : class
    {
        var eventType = typeof(TParticipant).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(ISagaParticipant<>))
            ?.GetGenericArguments()[0]
            ?? throw new InvalidOperationException(
                $"'{typeof(TParticipant).Name}' must implement ISagaParticipant<TEvent>.");

        services.TryAddTransient(typeof(TParticipant));

        var descriptorType = typeof(SagaParticipantDescriptor<,>)
            .MakeGenericType(typeof(TParticipant), eventType);
        var instance = (SagaParticipantDescriptor)Activator.CreateInstance(descriptorType)!;
        services.AddSingleton(instance);

        GetRegistry(services)?.RegisterEventType(eventType);

        return services;
    }

    private static Type? FindSagaStateType(Type orchestratorType)
    {
        for (var t = orchestratorType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(SagaOrchestrator<>))
                return t.GetGenericArguments()[0];
        }
        return null;
    }

    private static MessageHandlerRegistry? GetRegistry(IServiceCollection services)
        => services.FirstOrDefault(d => d.ImplementationInstance is MessageHandlerRegistry)
               ?.ImplementationInstance as MessageHandlerRegistry;
}
