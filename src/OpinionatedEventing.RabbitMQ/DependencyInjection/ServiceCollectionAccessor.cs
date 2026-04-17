#nullable enable

using Microsoft.Extensions.DependencyInjection;

namespace OpinionatedEventing.RabbitMQ.DependencyInjection;

/// <summary>
/// Captures the <see cref="IServiceCollection"/> at registration time so that the consumer
/// worker and topology initializer can scan registered handler types at host startup.
/// </summary>
internal sealed class ServiceCollectionAccessor
{
    internal IServiceCollection Services { get; }

    internal ServiceCollectionAccessor(IServiceCollection services) => Services = services;
}
