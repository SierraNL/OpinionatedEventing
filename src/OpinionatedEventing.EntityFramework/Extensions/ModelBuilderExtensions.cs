using Microsoft.EntityFrameworkCore;
using OpinionatedEventing.EntityFramework.Configuration;

// Placing in this namespace so the extension is available without an extra using directive.
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods on <see cref="ModelBuilder"/> for OpinionatedEventing EF Core configuration.
/// </summary>
public static class OpinionatedEventingModelBuilderExtensions
{
    /// <summary>
    /// Applies the <c>outbox_messages</c> table configuration to the model.
    /// Call this inside <c>OnModelCreating</c> to include the outbox table in your schema.
    /// </summary>
    /// <param name="modelBuilder">The model builder to configure.</param>
    /// <returns>The same <paramref name="modelBuilder"/> for chaining.</returns>
    public static ModelBuilder ApplyOutboxConfiguration(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration());
        return modelBuilder;
    }

    /// <summary>
    /// Applies the <c>saga_states</c> table configuration to the model.
    /// Call this inside <c>OnModelCreating</c> to include the saga state table in your schema.
    /// </summary>
    /// <param name="modelBuilder">The model builder to configure.</param>
    /// <returns>The same <paramref name="modelBuilder"/> for chaining.</returns>
    public static ModelBuilder ApplySagaStateConfiguration(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SagaStateEntityTypeConfiguration());
        return modelBuilder;
    }
}
