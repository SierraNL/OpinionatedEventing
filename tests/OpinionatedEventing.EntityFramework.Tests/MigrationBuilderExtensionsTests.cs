#nullable enable

using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using OpinionatedEventing.EntityFramework.Tests.TestSupport;
using Xunit;

namespace OpinionatedEventing.EntityFramework.Tests;

/// <summary>
/// Tests for <see cref="OpinionatedEventingMigrationBuilderExtensions"/>.
/// Tests that execute DDL against a real SQL Server instance are tagged
/// <c>Category=Integration</c> and require Docker.
/// </summary>
public sealed class MigrationBuilderExtensionsTests : IClassFixture<MigrationTestFixture>
{
    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    private readonly MigrationTestFixture _fixture;

    /// <summary>Initialises the test class with the shared SQL Server fixture.</summary>
    public MigrationBuilderExtensionsTests(MigrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact, Trait("Category", "Integration")]
    public void CreateOutboxTable_creates_table_and_both_indexes()
    {
        using var ctx = BuildContext();
        var generator = ctx.GetInfrastructure().GetRequiredService<IMigrationsSqlGenerator>();

        var builder = new MigrationBuilder(SqlServerProvider);
        builder.CreateOutboxTable();
        Apply(ctx, generator, builder);

        Assert.True(TableExists(ctx, "outbox_messages"));
        Assert.True(IndexExists(ctx, "IX_outbox_messages_pending"));
        Assert.True(IndexExists(ctx, "IX_outbox_messages_lock"));

        // Cleanup so subsequent tests start with a clean slate.
        Drop(ctx, generator, b => b.DropOutboxTable());
    }

    [Fact, Trait("Category", "Integration")]
    public void DropOutboxTable_removes_table_and_index()
    {
        using var ctx = BuildContext();
        var generator = ctx.GetInfrastructure().GetRequiredService<IMigrationsSqlGenerator>();

        Apply(ctx, generator, new MigrationBuilder(SqlServerProvider), b => b.CreateOutboxTable());
        Apply(ctx, generator, new MigrationBuilder(SqlServerProvider), b => b.DropOutboxTable());

        Assert.False(TableExists(ctx, "outbox_messages"));
    }

    [Fact, Trait("Category", "Integration")]
    public void CreateSagaStateTable_creates_table_unique_and_timeout_indexes()
    {
        using var ctx = BuildContext();
        var generator = ctx.GetInfrastructure().GetRequiredService<IMigrationsSqlGenerator>();

        var builder = new MigrationBuilder(SqlServerProvider);
        builder.CreateSagaStateTable();
        Apply(ctx, generator, builder);

        Assert.True(TableExists(ctx, "saga_states"));
        Assert.True(IndexExists(ctx, "UX_saga_states_type_correlation"));
        Assert.True(IndexExists(ctx, "IX_saga_states_timeout"));

        Drop(ctx, generator, b => b.DropSagaStateTable());
    }

    [Fact, Trait("Category", "Integration")]
    public void DropSagaStateTable_removes_table_and_indexes()
    {
        using var ctx = BuildContext();
        var generator = ctx.GetInfrastructure().GetRequiredService<IMigrationsSqlGenerator>();

        Apply(ctx, generator, new MigrationBuilder(SqlServerProvider), b => b.CreateSagaStateTable());
        Apply(ctx, generator, new MigrationBuilder(SqlServerProvider), b => b.DropSagaStateTable());

        Assert.False(TableExists(ctx, "saga_states"));
    }

    // --- pure-operations tests (no database required) ---

    [Fact]
    public void CreateOutboxTable_queues_CreateTable_and_CreateIndex_operations()
    {
        var builder = new MigrationBuilder(SqlServerProvider);

        builder.CreateOutboxTable();

        var createTable = Assert.Single(builder.Operations.OfType<CreateTableOperation>());
        Assert.Equal("outbox_messages", createTable.Name);
        Assert.Contains(createTable.Columns, c => c.Name == "LockedUntil");
        Assert.Contains(createTable.Columns, c => c.Name == "LockedBy");

        var indexes = builder.Operations.OfType<CreateIndexOperation>().ToList();
        Assert.Contains(indexes, i => i.Name == "IX_outbox_messages_pending");
        Assert.Contains(indexes, i => i.Name == "IX_outbox_messages_lock");
    }

    [Fact]
    public void CreateSagaStateTable_queues_CreateTable_and_two_CreateIndex_operations()
    {
        var builder = new MigrationBuilder(SqlServerProvider);

        builder.CreateSagaStateTable();

        var createTable = Assert.Single(builder.Operations.OfType<CreateTableOperation>());
        Assert.Equal("saga_states", createTable.Name);
        Assert.Contains(createTable.Columns, c => c.Name == "LockedUntil");
        Assert.Contains(createTable.Columns, c => c.Name == "LockedBy");
        Assert.Equal(2, builder.Operations.OfType<CreateIndexOperation>().Count());
    }

    [Fact]
    public void AddSagaStateLockColumns_queues_AddColumn_DropIndex_and_CreateIndex_operations()
    {
        var builder = new MigrationBuilder(SqlServerProvider);

        builder.AddSagaStateLockColumns();

        var addColumns = builder.Operations.OfType<AddColumnOperation>().ToList();
        Assert.Contains(addColumns, c => c.Name == "LockedBy");
        Assert.Contains(addColumns, c => c.Name == "LockedUntil");

        var dropIndex = Assert.Single(builder.Operations.OfType<DropIndexOperation>());
        Assert.Equal("IX_saga_states_timeout", dropIndex.Name);

        var createIndex = Assert.Single(builder.Operations.OfType<CreateIndexOperation>());
        Assert.Equal("IX_saga_states_timeout", createIndex.Name);
        Assert.Contains("LockedUntil", createIndex.Columns);
    }

    [Fact]
    public void All_extension_methods_return_the_same_builder_for_fluent_chaining()
    {
        var b1 = new MigrationBuilder(SqlServerProvider);
        var b2 = new MigrationBuilder(SqlServerProvider);
        var b3 = new MigrationBuilder(SqlServerProvider);
        var b4 = new MigrationBuilder(SqlServerProvider);
        var b5 = new MigrationBuilder(SqlServerProvider);

        Assert.Same(b1, b1.CreateOutboxTable());
        Assert.Same(b2, b2.DropOutboxTable());
        Assert.Same(b3, b3.CreateSagaStateTable());
        Assert.Same(b4, b4.DropSagaStateTable());
        Assert.Same(b5, b5.AddSagaStateLockColumns());
    }

    [Fact]
    public void CreateSagaStateTable_emits_long_columns_for_SQLite_provider()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");

        builder.CreateSagaStateTable();

        var createTable = Assert.Single(builder.Operations.OfType<CreateTableOperation>());
        Assert.Equal(typeof(long), createTable.Columns.Single(c => c.Name == "ExpiresAt").ClrType);
        Assert.Equal(typeof(long), createTable.Columns.Single(c => c.Name == "LockedUntil").ClrType);
    }

    [Fact]
    public void AddSagaStateLockColumns_emits_long_LockedUntil_for_SQLite_provider()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");

        builder.AddSagaStateLockColumns();

        var addColumns = builder.Operations.OfType<AddColumnOperation>().ToList();
        Assert.Equal(typeof(long), addColumns.Single(c => c.Name == "LockedUntil").ClrType);
        Assert.Equal(typeof(string), addColumns.Single(c => c.Name == "LockedBy").ClrType);
    }

    // --- helpers ---

    private MigrationContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<MigrationContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options;
        return new MigrationContext(options);
    }

    private static void Apply(
        DbContext ctx,
        IMigrationsSqlGenerator generator,
        MigrationBuilder builder,
        Action<MigrationBuilder>? configure = null)
    {
        configure?.Invoke(builder);
        foreach (var command in generator.Generate(builder.Operations))
            ctx.Database.ExecuteSqlRaw(command.CommandText);
    }

    private static void Drop(
        DbContext ctx,
        IMigrationsSqlGenerator generator,
        Action<MigrationBuilder> configure)
    {
        var b = new MigrationBuilder(SqlServerProvider);
        configure(b);
        foreach (var command in generator.Generate(b.Operations))
            ctx.Database.ExecuteSqlRaw(command.CommandText);
    }

    private static bool TableExists(DbContext ctx, string tableName)
    {
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            ctx.Database.OpenConnection();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = @name";
        var param = cmd.CreateParameter();
        param.ParameterName = "@name";
        param.Value = tableName;
        cmd.Parameters.Add(param);
        return (int)cmd.ExecuteScalar()! == 1;
    }

    private static bool IndexExists(DbContext ctx, string indexName)
    {
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            ctx.Database.OpenConnection();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.indexes WHERE name = @name";
        var param = cmd.CreateParameter();
        param.ParameterName = "@name";
        param.Value = indexName;
        cmd.Parameters.Add(param);
        return (int)cmd.ExecuteScalar()! == 1;
    }

    // Minimal DbContext whose only purpose is to expose the SQL Server migration services.
    private sealed class MigrationContext : DbContext
    {
        public MigrationContext(DbContextOptions<MigrationContext> options) : base(options) { }
    }
}
