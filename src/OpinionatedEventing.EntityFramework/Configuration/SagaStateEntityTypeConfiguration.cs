using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpinionatedEventing.Sagas;

namespace OpinionatedEventing.EntityFramework.Configuration;

/// <summary>
/// EF Core entity type configuration for <see cref="SagaState"/>.
/// Maps the saga state table to <c>saga_states</c> with an index optimised for timeout polling.
/// </summary>
/// <remarks>
/// Apply via <c>modelBuilder.ApplySagaStateConfiguration()</c> inside <c>OnModelCreating</c>,
/// or let EF Core discover it automatically via <c>modelBuilder.ApplyConfigurationsFromAssembly</c>.
/// </remarks>
public sealed class SagaStateEntityTypeConfiguration : IEntityTypeConfiguration<SagaState>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<SagaState> builder)
    {
        builder.ToTable("saga_states");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.SagaType).IsRequired().HasMaxLength(512);
        builder.Property(s => s.CorrelationId).IsRequired().HasMaxLength(256);
        builder.Property(s => s.State).IsRequired();
        builder.Property(s => s.Status).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();
        builder.Property(s => s.ExpiresAt);

        // Unique constraint: one instance per saga type + correlation ID
        builder.HasIndex(s => new { s.SagaType, s.CorrelationId })
            .IsUnique()
            .HasDatabaseName("UX_saga_states_type_correlation");

        // Supports efficient timeout polling: WHERE Status = Active AND ExpiresAt <= now
        builder.HasIndex(s => new { s.Status, s.ExpiresAt })
            .HasDatabaseName("IX_saga_states_timeout");
    }
}
