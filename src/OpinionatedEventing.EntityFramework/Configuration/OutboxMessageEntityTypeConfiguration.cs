using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.EntityFramework.Configuration;

/// <summary>
/// EF Core entity type configuration for <see cref="OutboxMessage"/>.
/// Maps the outbox table to <c>outbox_messages</c> with an index optimised for pending-message queries.
/// </summary>
/// <remarks>
/// Apply via <c>modelBuilder.ApplyOutboxConfiguration()</c> inside <c>OnModelCreating</c>,
/// or let EF Core discover it automatically via <c>modelBuilder.ApplyConfigurationsFromAssembly</c>.
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
        builder.Property(m => m.MessageKind).IsRequired().HasMaxLength(16);
        builder.Property(m => m.CorrelationId).IsRequired();
        builder.Property(m => m.CausationId);
        builder.Property(m => m.CreatedAt).IsRequired();
        builder.Property(m => m.ProcessedAt);
        builder.Property(m => m.FailedAt);
        builder.Property(m => m.AttemptCount).IsRequired().HasDefaultValue(0);
        builder.Property(m => m.Error);

        // Supports efficient pending-message polling: WHERE ProcessedAt IS NULL AND FailedAt IS NULL ORDER BY CreatedAt
        builder.HasIndex(m => new { m.ProcessedAt, m.FailedAt, m.CreatedAt })
            .HasDatabaseName("IX_outbox_messages_pending");
    }
}
