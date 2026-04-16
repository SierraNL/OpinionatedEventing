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
    /// <summary>Creates the <c>outbox_messages</c> table.</summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <returns>The same <paramref name="migrationBuilder"/> for chaining.</returns>
    public static MigrationBuilder CreateOutboxTable(this MigrationBuilder migrationBuilder)
    {
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
                CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(nullable: true),
                FailedAt = table.Column<DateTimeOffset>(nullable: true),
                AttemptCount = table.Column<int>(nullable: false, defaultValue: 0),
                Error = table.Column<string>(nullable: true),
            },
            constraints: table =>
                table.PrimaryKey("PK_outbox_messages", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_pending",
            table: "outbox_messages",
            columns: ["ProcessedAt", "FailedAt", "CreatedAt"]);

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

    /// <summary>Creates the <c>saga_states</c> table.</summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <returns>The same <paramref name="migrationBuilder"/> for chaining.</returns>
    public static MigrationBuilder CreateSagaStateTable(this MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "saga_states",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                SagaType = table.Column<string>(maxLength: 512, nullable: false),
                CorrelationId = table.Column<string>(maxLength: 256, nullable: false),
                State = table.Column<string>(nullable: false),
                Status = table.Column<int>(nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(nullable: true),
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
