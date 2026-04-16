using Microsoft.EntityFrameworkCore;

namespace OpinionatedEventing.EntityFramework.Tests.TestSupport;

/// <summary>
/// Creates <see cref="TestDbContext"/> instances backed by the EF Core in-memory provider.
/// All contexts from the same factory share the same named database, so writes in one
/// context are visible to a subsequent context — matching the isolation level of a real DB
/// within a single test.
/// </summary>
internal sealed class InMemoryDbContextFactory : IDisposable
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    /// <summary>Returns a pre-configured options builder for the shared in-memory database.</summary>
    public DbContextOptionsBuilder<TestDbContext> CreateOptionsBuilder()
        => new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(_databaseName);

    /// <summary>Creates a new <see cref="TestDbContext"/> connected to the shared in-memory database.</summary>
    public TestDbContext CreateContext()
        => new(CreateOptionsBuilder().Options);

    public void Dispose() { }
}
