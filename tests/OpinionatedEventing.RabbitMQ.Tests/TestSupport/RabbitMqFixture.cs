#nullable enable

using Testcontainers.RabbitMq;
using Xunit;

namespace OpinionatedEventing.RabbitMQ.Tests.TestSupport;

/// <summary>Shared RabbitMQ container fixture — one container for all integration tests.</summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private RabbitMqContainer? _container;

    /// <summary>Gets the connection string for the running RabbitMQ container.</summary>
    // InitializeAsync guarantees _container is set before any test accesses this property
    public string ConnectionString => _container!.GetConnectionString();

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        _container = new RabbitMqBuilder().Build();
        await _container.StartAsync(CancellationToken.None);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
