using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.EntityFramework.Configuration;

/// <summary>
/// EF Core entity type configuration for <see cref="OutboxMessage"/>.
/// Maps the outbox table to <c>outbox_messages</c> with an index optimised for pending-message queries.
/// </summary>
/// <remarks>
/// <para>
/// Apply via <c>modelBuilder.ApplyOutboxConfiguration(Database.ProviderName)</c> inside
/// <c>OnModelCreating</c>. Passing <c>Database.ProviderName</c> enables the automatic
/// SQLite value-converter that stores <see cref="DateTimeOffset"/> columns as UTC ticks,
/// preserving sort order on the pending-message index.
/// </para>
/// <para>
/// When using <c>modelBuilder.ApplyConfigurationsFromAssembly</c>, this configuration is
/// discovered automatically but the SQLite converters are <b>not</b> applied — call
/// <c>modelBuilder.ApplyOutboxConfiguration(Database.ProviderName)</c> explicitly instead.
/// </para>
/// </remarks>
public sealed class OutboxMessageEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.MessageType).IsRequired().HasMaxLength(512);
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.MessageKind).IsRequired().HasMaxLength(16).HasConversion<string>();
        builder.Property(m => m.CorrelationId).IsRequired();
        builder.Property(m => m.CausationId);
        builder.Property(m => m.CreatedAt).IsRequired();
        builder.Property(m => m.ProcessedAt);
        builder.Property(m => m.FailedAt);
        builder.Property(m => m.AttemptCount).IsRequired().HasDefaultValue(0);
        builder.Property(m => m.Error);
        builder.Property(m => m.LockedUntil);
        builder.Property(m => m.LockedBy).HasMaxLength(36);
        builder.Property(m => m.NextAttemptAt);

        // Supports efficient pending-message polling: WHERE ProcessedAt IS NULL AND FailedAt IS NULL ORDER BY CreatedAt
        builder.HasIndex(m => new { m.ProcessedAt, m.FailedAt, m.CreatedAt })
            .HasDatabaseName("IX_outbox_messages_pending");

        // Supports lock-expiry re-dispatch: WHERE LockedUntil IS NULL OR LockedUntil < @now
        builder.HasIndex(m => new { m.LockedUntil, m.ProcessedAt, m.FailedAt })
            .HasDatabaseName("IX_outbox_messages_lock");

        // Supports dead-letter cleanup: DELETE WHERE FailedAt IS NOT NULL AND FailedAt < @cutoff
        builder.HasIndex(m => m.FailedAt)
            .HasDatabaseName("IX_outbox_messages_cleanup_failed");
    }
}
