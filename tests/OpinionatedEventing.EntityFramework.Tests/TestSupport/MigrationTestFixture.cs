#nullable enable

using Testcontainers.MsSql;
using Xunit;

namespace OpinionatedEventing.EntityFramework.Tests.TestSupport;

/// <summary>
/// xUnit fixture that starts a SQL Server container for migration integration tests.
/// One container is shared across all tests in the same test class.
/// </summary>
public sealed class MigrationTestFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    /// <summary>Gets the SQL Server connection string for the running container.</summary>
    // InitializeAsync guarantees _container is non-null before any test accesses this.
    public string ConnectionString => _container!.GetConnectionString();

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .Build();
        await _container.StartAsync();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
