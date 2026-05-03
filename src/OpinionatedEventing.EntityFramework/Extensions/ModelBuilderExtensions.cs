using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpinionatedEventing.EntityFramework.Configuration;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Sagas;

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
    /// <param name="providerName">
    /// The active database provider name (e.g. <c>Database.ProviderName</c>).
    /// When the value contains <c>"Sqlite"</c> (case-insensitive), a
    /// <see cref="ValueConverter{TModel,TProvider}"/> that stores <see cref="DateTimeOffset"/>
    /// as UTC ticks (<c>long</c> / <c>INTEGER</c>) is applied to all <see cref="DateTimeOffset"/>
    /// columns, preserving sort order on SQLite.
    /// </param>
    /// <returns>The same <paramref name="modelBuilder"/> for chaining.</returns>
    public static ModelBuilder ApplyOutboxConfiguration(this ModelBuilder modelBuilder, string? providerName = null)
    {
        modelBuilder.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration());

        if (IsSqlite(providerName))
            ApplyOutboxSqliteConverters(modelBuilder);

        return modelBuilder;
    }

    /// <summary>
    /// Applies the <c>saga_states</c> table configuration to the model.
    /// Call this inside <c>OnModelCreating</c> to include the saga state table in your schema.
    /// </summary>
    /// <param name="modelBuilder">The model builder to configure.</param>
    /// <param name="providerName">
    /// The active database provider name (e.g. <c>Database.ProviderName</c>).
    /// When the value contains <c>"Sqlite"</c> (case-insensitive), a
    /// <see cref="ValueConverter{TModel,TProvider}"/> that stores <see cref="DateTimeOffset"/>
    /// as UTC ticks (<c>long</c> / <c>INTEGER</c>) is applied to all <see cref="DateTimeOffset"/>
    /// columns, preserving sort order on SQLite.
    /// </param>
    /// <returns>The same <paramref name="modelBuilder"/> for chaining.</returns>
    public static ModelBuilder ApplySagaStateConfiguration(this ModelBuilder modelBuilder, string? providerName = null)
    {
        modelBuilder.ApplyConfiguration(new SagaStateEntityTypeConfiguration());

        if (IsSqlite(providerName))
            ApplySagaStateSqliteConverters(modelBuilder);

        return modelBuilder;
    }

    private static bool IsSqlite(string? providerName)
        => providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    // Stores DateTimeOffset as UTC ticks (long/INTEGER) on SQLite.
    // SQLite has no native DateTimeOffset type; without this, EF stores it as TEXT, breaking
    // ORDER BY correctness on the pending-message and saga-timeout indexes.
    // The converter is stateless (pure functions, no captured state) — safe to share across
    // multiple HasConversion calls and across context instances.
    private static readonly ValueConverter<DateTimeOffset, long> DateTimeOffsetToTicks =
        new(dto => dto.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    private static void ApplyOutboxSqliteConverters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.Property(m => m.CreatedAt).HasConversion(DateTimeOffsetToTicks);
            b.Property(m => m.ProcessedAt).HasConversion(DateTimeOffsetToTicks);
            b.Property(m => m.FailedAt).HasConversion(DateTimeOffsetToTicks);
            b.Property(m => m.LockedUntil).HasConversion(DateTimeOffsetToTicks);
            b.Property(m => m.NextAttemptAt).HasConversion(DateTimeOffsetToTicks);
        });
    }

    private static void ApplySagaStateSqliteConverters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SagaState>(b =>
        {
            b.Property(s => s.CreatedAt).HasConversion(DateTimeOffsetToTicks);
            b.Property(s => s.UpdatedAt).HasConversion(DateTimeOffsetToTicks);
            b.Property(s => s.ExpiresAt).HasConversion(DateTimeOffsetToTicks);
            b.Property(s => s.LockedUntil).HasConversion(DateTimeOffsetToTicks);
        });
    }
}
