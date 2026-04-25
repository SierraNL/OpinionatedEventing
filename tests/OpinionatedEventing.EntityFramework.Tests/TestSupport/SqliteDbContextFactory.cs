using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace OpinionatedEventing.EntityFramework.Tests.TestSupport;

/// <summary>
/// Creates <see cref="SqliteTestDbContext"/> instances backed by a shared in-process SQLite database.
/// Uses a persistent <see cref="SqliteConnection"/> so the in-memory database survives across
/// multiple context instances within the same test.
/// </summary>
internal sealed class SqliteDbContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteDbContextFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Ensure the schema is created once for the shared connection.
        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();
    }

    /// <summary>Creates a new <see cref="SqliteTestDbContext"/> sharing the same in-memory database.</summary>
    public SqliteTestDbContext CreateContext()
        => new(new DbContextOptionsBuilder<SqliteTestDbContext>()
            .UseSqlite(_connection)
            .Options);

    public void Dispose() => _connection.Dispose();
}
