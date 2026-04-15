#nullable enable

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpinionatedEventing;
using OpinionatedEventing.DependencyInjection;
using OpinionatedEventing.Outbox;

// Placed in this namespace so the extension is available without an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="OpinionatedEventingBuilder"/> for registering the outbox services.
/// </summary>
public static class OutboxBuilderExtensions
{
    /// <summary>
    /// Registers the outbox dispatcher services: <see cref="IPublisher"/> implementation and
    /// <see cref="OutboxDispatcherWorker"/> hosted service.
    /// </summary>
    /// <remarks>
    /// Requires a registered <see cref="IOutboxStore"/> (provided by
    /// <c>OpinionatedEventing.EntityFramework</c> or <c>OpinionatedEventing.Testing</c>) and a
    /// registered <see cref="ITransport"/> (provided by <c>OpinionatedEventing.AzureServiceBus</c>
    /// or <c>OpinionatedEventing.RabbitMQ</c>).
    /// </remarks>
    /// <param name="builder">The eventing builder to extend.</param>
    /// <returns>The same builder for chaining.</returns>
    public static OpinionatedEventingBuilder AddOutbox(this OpinionatedEventingBuilder builder)
    {
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.TryAddScoped<IPublisher, OutboxPublisher>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, OutboxDispatcherWorker>());
        return builder;
    }
}
