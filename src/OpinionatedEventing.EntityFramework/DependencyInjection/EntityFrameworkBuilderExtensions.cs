using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpinionatedEventing.EntityFramework;
using OpinionatedEventing.EntityFramework.Sagas;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Sagas;

// Placing in this namespace so the extension is available without an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering
/// OpinionatedEventing EF Core services.
/// </summary>
public static class EntityFrameworkBuilderExtensions
{
    /// <summary>
    /// Registers the EF Core implementations of <see cref="IOutboxStore"/> and
    /// <see cref="ISagaStateStore"/>, and registers the <see cref="DomainEventInterceptor"/>
    /// for use with the application's <typeparamref name="TDbContext"/>.
    /// </summary>
    /// <typeparam name="TDbContext">
    /// The application's <see cref="DbContext"/> type. Must be registered in the DI container
    /// before or after this call.
    /// </typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Wire the interceptor into your <c>DbContext</c> configuration by adding
    /// <c>options.AddInterceptors(sp.GetRequiredService&lt;DomainEventInterceptor&gt;())</c>
    /// inside your <c>AddDbContext</c> delegate. The <c>sp</c> parameter is the
    /// <em>scoped</em> service provider supplied by EF Core's factory; resolving from it
    /// ensures that <c>IMessagingContext</c> is obtained per request and not captured
    /// once from the root container:
    /// </para>
    /// <code>
    /// services.AddDbContext&lt;AppDbContext&gt;((sp, options) =>
    /// {
    ///     options.UseSqlServer(connectionString);
    ///     options.AddInterceptors(sp.GetRequiredService&lt;DomainEventInterceptor&gt;());
    /// });
    /// services.AddOpinionatedEventingEntityFramework&lt;AppDbContext&gt;();
    /// </code>
    /// <para>
    /// The <c>outbox_messages</c> and <c>saga_states</c> tables must be included in the
    /// <c>DbContext</c> model. Call <c>modelBuilder.ApplyOutboxConfiguration()</c> and
    /// <c>modelBuilder.ApplySagaStateConfiguration()</c> inside <c>OnModelCreating</c>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOpinionatedEventingEntityFramework<TDbContext>(
        this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.TryAddScoped<IOutboxStore, EFCoreOutboxStore<TDbContext>>();
        services.TryAddScoped<ISagaStateStore, EFCoreSagaStateStore<TDbContext>>();
        services.TryAddScoped<IOutboxTransactionGuard, EFCoreOutboxTransactionGuard<TDbContext>>();
        services.TryAddScoped<IOutboxMonitor, EFCoreOutboxMonitor<TDbContext>>();
        services.TryAddScoped<DomainEventInterceptor>();

        return services;
    }
}
