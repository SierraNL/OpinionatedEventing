#nullable enable

using Xunit;

namespace OpinionatedEventing.AzureServiceBus.Tests.TestSupport;

/// <summary>Shared Azure Service Bus emulator fixture — one container for all integration tests.</summary>
public sealed class AzureServiceBusFixture : IAsyncLifetime
{
    private AzureServiceBusEmulatorContainer? _emulator;

    /// <summary>Gets the AMQP connection string for the running Azure Service Bus emulator.</summary>
    // InitializeAsync guarantees _emulator is set before any test accesses this property
    public string ConnectionString => _emulator!.ConnectionString;

    /// <summary>Gets the management (HTTP port 5300) connection string for the running emulator.</summary>
    public string ManagementConnectionString => _emulator!.ManagementConnectionString;

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        _emulator = await AzureServiceBusEmulatorContainer.StartAsync(CancellationToken.None);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_emulator is not null)
            await _emulator.DisposeAsync();
    }
}
