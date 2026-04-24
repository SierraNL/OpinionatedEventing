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
/// Integration tests for <see cref="OpinionatedEventingMigrationBuilderExtensions"/>.
/// Require Docker with a SQL Server image available.
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public sealed class MigrationBuilderExtensionsTests : IClassFixture<MigrationTestFixture>
{
    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    private readonly MigrationTestFixture _fixture;

    /// <summary>Initialises the test class with the shared SQL Server fixture.</summary>
    public MigrationBuilderExtensionsTests(MigrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void CreateOutboxTable_creates_table_and_pending_index()
    {
        using var ctx = BuildContext();
        var generator = ctx.GetInfrastructure().GetRequiredService<IMigrationsSqlGenerator>();

        var builder = new MigrationBuilder(SqlServerProvider);
        builder.CreateOutboxTable();
        Apply(ctx, generator, builder);

        Assert.True(TableExists(ctx, "outbox_messages"));
        Assert.True(IndexExists(ctx, "IX_outbox_messages_pending"));

        // Cleanup so subsequent tests start with a clean slate.
        Drop(ctx, generator, b => b.DropOutboxTable());
    }

    [Fact]
    public void DropOutboxTable_removes_table_and_index()
    {
        using var ctx = BuildContext();
        var generator = ctx.GetInfrastructure().GetRequiredService<IMigrationsSqlGenerator>();

        Apply(ctx, generator, new MigrationBuilder(SqlServerProvider), b => b.CreateOutboxTable());
        Apply(ctx, generator, new MigrationBuilder(SqlServerProvider), b => b.DropOutboxTable());

        Assert.False(TableExists(ctx, "outbox_messages"));
    }

    [Fact]
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

    [Fact]
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
        Assert.Single(builder.Operations.OfType<CreateIndexOperation>(),
            i => i.Name == "IX_outbox_messages_pending");
    }

    [Fact]
    public void CreateSagaStateTable_queues_CreateTable_and_two_CreateIndex_operations()
    {
        var builder = new MigrationBuilder(SqlServerProvider);

        builder.CreateSagaStateTable();

        var createTable = Assert.Single(builder.Operations.OfType<CreateTableOperation>());
        Assert.Equal("saga_states", createTable.Name);
        Assert.Equal(2, builder.Operations.OfType<CreateIndexOperation>().Count());
    }

    [Fact]
    public void All_extension_methods_return_the_same_builder_for_fluent_chaining()
    {
        var b1 = new MigrationBuilder(SqlServerProvider);
        var b2 = new MigrationBuilder(SqlServerProvider);
        var b3 = new MigrationBuilder(SqlServerProvider);
        var b4 = new MigrationBuilder(SqlServerProvider);

        Assert.Same(b1, b1.CreateOutboxTable());
        Assert.Same(b2, b2.DropOutboxTable());
        Assert.Same(b3, b3.CreateSagaStateTable());
        Assert.Same(b4, b4.DropSagaStateTable());
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
