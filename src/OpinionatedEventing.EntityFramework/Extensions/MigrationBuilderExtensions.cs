using Microsoft.EntityFrameworkCore.Migrations;

// Placing in this namespace so the extension is available without an extra using directive.
namespace Microsoft.EntityFrameworkCore.Migrations;

/// <summary>
/// Extension methods on <see cref="MigrationBuilder"/> for OpinionatedEventing schema operations.
/// Use these helpers inside EF Core <c>Migration</c> classes to create or drop the outbox
/// and saga state tables.
/// </summary>
public static class OpinionatedEventingMigrationBuilderExtensions
{
    /// <summary>
    /// Creates the <c>outbox_messages</c> table.
    /// </summary>
    /// <remarks>
    /// When <see cref="MigrationBuilder.ActiveProvider"/> contains <c>"Sqlite"</c> (case-insensitive),
    /// <see cref="DateTimeOffset"/> columns (<c>CreatedAt</c>, <c>ProcessedAt</c>, <c>FailedAt</c>,
    /// <c>LockedUntil</c>) are emitted as <c>long</c> (<c>INTEGER</c>) to store UTC ticks, matching the
    /// <see cref="Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter{TModel,TProvider}"/>
    /// applied by <c>modelBuilder.ApplyOutboxConfiguration(Database.ProviderName)</c>.
    /// </remarks>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <returns>The same <paramref name="migrationBuilder"/> for chaining.</returns>
    public static MigrationBuilder CreateOutboxTable(this MigrationBuilder migrationBuilder)
    {
        // null-safe: ActiveProvider is non-nullable in EF Core 8–10, but defensive here;
        // a missing/null provider is treated as non-SQLite (correct default).
        var sqlite = migrationBuilder.ActiveProvider?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                MessageType = table.Column<string>(maxLength: 512, nullable: false),
                Payload = table.Column<string>(nullable: false),
                MessageKind = table.Column<string>(maxLength: 16, nullable: false),
                CorrelationId = table.Column<Guid>(nullable: false),
                CausationId = table.Column<Guid>(nullable: true),
                CreatedAt = sqlite ? table.Column<long>(nullable: false) : table.Column<DateTimeOffset>(nullable: false),
                ProcessedAt = sqlite ? table.Column<long>(nullable: true) : table.Column<DateTimeOffset>(nullable: true),
                FailedAt = sqlite ? table.Column<long>(nullable: true) : table.Column<DateTimeOffset>(nullable: true),
                AttemptCount = table.Column<int>(nullable: false, defaultValue: 0),
                Error = table.Column<string>(nullable: true),
                LockedUntil = sqlite ? table.Column<long>(nullable: true) : table.Column<DateTimeOffset>(nullable: true),
                LockedBy = table.Column<string>(maxLength: 36, nullable: true),
                NextAttemptAt = sqlite ? table.Column<long>(nullable: true) : table.Column<DateTimeOffset>(nullable: true),
            },
            constraints: table =>
                table.PrimaryKey("PK_outbox_messages", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_pending",
            table: "outbox_messages",
            columns: ["ProcessedAt", "FailedAt", "CreatedAt"]);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_lock",
            table: "outbox_messages",
            columns: ["LockedUntil", "ProcessedAt", "FailedAt"]);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_cleanup_failed",
            table: "outbox_messages",
            column: "FailedAt");

        return migrationBuilder;
    }

    /// <summary>Drops the <c>outbox_messages</c> table.</summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <returns>The same <paramref name="migrationBuilder"/> for chaining.</returns>
    public static MigrationBuilder DropOutboxTable(this MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages");
        return migrationBuilder;
    }

    /// <summary>
    /// Adds the <c>NextAttemptAt</c> column and <c>IX_outbox_messages_cleanup_failed</c> index
    /// to an existing <c>outbox_messages</c> table.
    /// </summary>
    /// <remarks>
    /// Use this in a new migration when upgrading an existing deployment that was created with
    /// an earlier version of <c>CreateOutboxTable</c> (before retry backoff and retention were added).
    /// New deployments that call <c>CreateOutboxTable</c> already include these schema elements.
    /// </remarks>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <returns>The same <paramref name="migrationBuilder"/> for chaining.</returns>
    public static MigrationBuilder AddOutboxRetentionColumns(this MigrationBuilder migrationBuilder)
    {
        var sqlite = migrationBuilder.ActiveProvider?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        if (sqlite)
        {
            migrationBuilder.AddColumn<long>(
                name: "NextAttemptAt",
                table: "outbox_messages",
                nullable: true);
        }
        else
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "outbox_messages",
                nullable: true);
        }

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_cleanup_failed",
            table: "outbox_messages",
            column: "FailedAt");

        return migrationBuilder;
    }

    /// <summary>
    /// Reverses <see cref="AddOutboxRetentionColumns"/>: drops the <c>NextAttemptAt</c> column
    /// and <c>IX_outbox_messages_cleanup_failed</c> index from <c>outbox_messages</c>.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <returns>The same <paramref name="migrationBuilder"/> for chaining.</returns>
    public static MigrationBuilder DropOutboxRetentionColumns(this MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_outbox_messages_cleanup_failed",
            table: "outbox_messages");

        migrationBuilder.DropColumn(
            name: "NextAttemptAt",
            table: "outbox_messages");

        return migrationBuilder;
    }

    /// <summary>
    /// Creates the <c>saga_states</c> table.
    /// </summary>
    /// <remarks>
    /// When <see cref="MigrationBuilder.ActiveProvider"/> contains <c>"Sqlite"</c> (case-insensitive),
    /// <see cref="DateTimeOffset"/> columns (<c>CreatedAt</c>, <c>UpdatedAt</c>, <c>ExpiresAt</c>) are
    /// emitted as <c>long</c> (<c>INTEGER</c>) to store UTC ticks, matching the
    /// <see cref="Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter{TModel,TProvider}"/>
    /// applied by <c>modelBuilder.ApplySagaStateConfiguration(Database.ProviderName)</c>.
    /// </remarks>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <returns>The same <paramref name="migrationBuilder"/> for chaining.</returns>
    public static MigrationBuilder CreateSagaStateTable(this MigrationBuilder migrationBuilder)
    {
        // null-safe: ActiveProvider is non-nullable in EF Core 8–10, but defensive here;
        // a missing/null provider is treated as non-SQLite (correct default).
        var sqlite = migrationBuilder.ActiveProvider?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        migrationBuilder.CreateTable(
            name: "saga_states",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                SagaType = table.Column<string>(maxLength: 512, nullable: false),
                CorrelationId = table.Column<string>(maxLength: 256, nullable: false),
                State = table.Column<string>(nullable: false),
                Status = table.Column<int>(nullable: false),
                CreatedAt = sqlite ? table.Column<long>(nullable: false) : table.Column<DateTimeOffset>(nullable: false),
                UpdatedAt = sqlite ? table.Column<long>(nullable: false) : table.Column<DateTimeOffset>(nullable: false),
                ExpiresAt = sqlite ? table.Column<long>(nullable: true) : table.Column<DateTimeOffset>(nullable: true),
            },
            constraints: table =>
                table.PrimaryKey("PK_saga_states", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "UX_saga_states_type_correlation",
            table: "saga_states",
            columns: ["SagaType", "CorrelationId"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_saga_states_timeout",
            table: "saga_states",
            columns: ["Status", "ExpiresAt"]);

        return migrationBuilder;
    }

    /// <summary>Drops the <c>saga_states</c> table.</summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <returns>The same <paramref name="migrationBuilder"/> for chaining.</returns>
    public static MigrationBuilder DropSagaStateTable(this MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "saga_states");
        return migrationBuilder;
    }
}
