#nullable enable

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace OpinionatedEventing.AzureServiceBus.Tests.TestSupport;

/// <summary>
/// Manages a local Azure Service Bus Emulator Docker container for integration tests.
/// </summary>
internal sealed class AzureServiceBusEmulatorContainer : IAsyncDisposable
{
    private const string Image = "mcr.microsoft.com/azure-messaging/servicebus-emulator:latest";
    private const int AmqpPort = 5672;

    // The emulator's hardcoded development connection string.
    private const string EmulatorConnectionStringTemplate =
        "Endpoint=sb://localhost:{0};SharedAccessKeyName=RootManageSharedAccessKey;" +
        "SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    private readonly IContainer _container;
    private readonly string _configPath;

    private AzureServiceBusEmulatorContainer(IContainer container, string configPath)
    {
        _container = container;
        _configPath = configPath;
    }

    /// <summary>
    /// Creates and starts the emulator with a namespace that has no pre-defined entities.
    /// Resources are created at runtime by the transport's auto-create feature.
    /// </summary>
    public static async Task<AzureServiceBusEmulatorContainer> StartAsync(
        CancellationToken ct = default)
    {
        var configJson = """
            {
              "UserConfig": {
                "Namespaces": [
                  {
                    "Name": "sbemulator",
                    "Queues": [],
                    "Topics": []
                  }
                ],
                "Logging": { "Type": "File" }
              }
            }
            """;

        // Write config to a temp file for volume mount.
        var configPath = Path.Combine(Path.GetTempPath(), $"asb-emulator-config-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, configJson, ct);

        var container = new ContainerBuilder()
            .WithImage(Image)
            .WithPortBinding(AmqpPort, true)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithBindMount(configPath, "/ServiceBus_Emulator/ConfigFiles/Config.json")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(AmqpPort))
            .Build();

        await container.StartAsync(ct);
        return new AzureServiceBusEmulatorContainer(container, configPath);
    }

    /// <summary>Gets the connection string pointing at the running emulator.</summary>
    public string ConnectionString
    {
        get
        {
            var port = _container.GetMappedPublicPort(AmqpPort);
            return string.Format(EmulatorConnectionStringTemplate, port);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        try { File.Delete(_configPath); } catch { /* best-effort cleanup */ }
    }
}
