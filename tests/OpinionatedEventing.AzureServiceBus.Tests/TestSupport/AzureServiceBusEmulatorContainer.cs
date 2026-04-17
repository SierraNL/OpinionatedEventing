#nullable enable

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace OpinionatedEventing.AzureServiceBus.Tests.TestSupport;

/// <summary>
/// Manages a local Azure Service Bus Emulator Docker container for integration tests.
/// The emulator requires a companion SQL Edge container; both share a private Docker network.
/// </summary>
internal sealed class AzureServiceBusEmulatorContainer : IAsyncDisposable
{
    private const string EmulatorImage = "mcr.microsoft.com/azure-messaging/servicebus-emulator:latest";
    private const string SqlEdgeImage = "mcr.microsoft.com/azure-sql-edge:latest";
    private const int AmqpPort = 5672;
    private const int SqlPort = 1433;
    private const string SqlAlias = "sqledge";
    private const string SqlPassword = "SqlEdgeP@ssw0rd!";

    // The emulator's hardcoded development connection string.
    private const string EmulatorConnectionStringTemplate =
        "Endpoint=sb://localhost:{0};SharedAccessKeyName=RootManageSharedAccessKey;" +
        "SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    private readonly INetwork _network;
    private readonly IContainer _sqlContainer;
    private readonly IContainer _emulatorContainer;
    private readonly string _configPath;

    private AzureServiceBusEmulatorContainer(
        INetwork network,
        IContainer sqlContainer,
        IContainer emulatorContainer,
        string configPath)
    {
        _network = network;
        _sqlContainer = sqlContainer;
        _emulatorContainer = emulatorContainer;
        _configPath = configPath;
    }

    /// <summary>
    /// Creates and starts the SQL Edge sidecar and ASB emulator containers.
    /// Resources are created at runtime by the transport's auto-create feature.
    /// </summary>
    public static async Task<AzureServiceBusEmulatorContainer> StartAsync(
        CancellationToken ct = default)
    {
        // The emulator only accepts the fixed namespace name "sbemulatorns".
        // The admin REST API is not exposed on the AMQP port, so entities must be
        // pre-defined here rather than created via ServiceBusAdministrationClient.
        var configJson = """
            {
              "UserConfig": {
                "Namespaces": [
                  {
                    "Name": "sbemulatorns",
                    "Queues": [
                      {
                        "Name": "process-payment",
                        "Properties": { "MaxDeliveryCount": 10 }
                      }
                    ],
                    "Topics": [
                      {
                        "Name": "order-placed",
                        "Properties": {},
                        "Subscriptions": [
                          {
                            "Name": "test-service",
                            "Properties": {}
                          }
                        ]
                      }
                    ]
                  }
                ],
                "Logging": { "Type": "File" }
              }
            }
            """;

        var configPath = Path.Combine(Path.GetTempPath(), $"asb-emulator-config-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, configJson, ct);

        var network = new NetworkBuilder().Build();
        await network.CreateAsync(ct);

        var sqlContainer = new ContainerBuilder()
            .WithImage(SqlEdgeImage)
            .WithNetwork(network)
            .WithNetworkAliases(SqlAlias)
            .WithEnvironment("ACCEPT_EULA", "1")
            .WithEnvironment("MSSQL_SA_PASSWORD", SqlPassword)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(SqlPort))
            .Build();

        await sqlContainer.StartAsync(ct);

        var emulatorContainer = new ContainerBuilder()
            .WithImage(EmulatorImage)
            .WithNetwork(network)
            .WithPortBinding(AmqpPort, true)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("SQL_SERVER", SqlAlias)
            .WithEnvironment("MSSQL_SA_PASSWORD", SqlPassword)
            .WithBindMount(configPath, "/ServiceBus_Emulator/ConfigFiles/Config.json")
            // The emulator image is distroless (no sh/nc/grep), so port-scan wait strategies
            // never succeed. Wait for the log line the emulator emits when it is ready instead.
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("Emulator Service is Successfully Up!"))
            .Build();

        await emulatorContainer.StartAsync(ct);

        return new AzureServiceBusEmulatorContainer(network, sqlContainer, emulatorContainer, configPath);
    }

    /// <summary>Gets the connection string pointing at the running emulator.</summary>
    public string ConnectionString
    {
        get
        {
            var port = _emulatorContainer.GetMappedPublicPort(AmqpPort);
            return string.Format(EmulatorConnectionStringTemplate, port);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _emulatorContainer.DisposeAsync();
        await _sqlContainer.DisposeAsync();
        await _network.DisposeAsync();
        try { File.Delete(_configPath); } catch { /* best-effort cleanup */ }
    }
}
