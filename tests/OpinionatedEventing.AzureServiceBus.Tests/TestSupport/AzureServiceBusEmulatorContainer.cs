#nullable enable

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.MsSql;

namespace OpinionatedEventing.AzureServiceBus.Tests.TestSupport;

/// <summary>
/// Manages a local Azure Service Bus Emulator Docker container for integration tests.
/// The emulator requires a SQL Server sidecar; both run on an isolated Docker network.
/// </summary>
internal sealed class AzureServiceBusEmulatorContainer : IAsyncDisposable
{
    private const string EmulatorImage = "mcr.microsoft.com/azure-messaging/servicebus-emulator:latest";
    private const int AmqpPort = 5672;
    private const int ManagementPort = 5300;

    // SA password shared between SQL Server and the emulator's env vars.
    private const string SqlPassword = "Strong@Passw0rd!";
    private const string SqlAlias = "sqlserver";

    // The emulator's hardcoded development connection string.
    private const string ConnectionStringTemplate =
        "Endpoint=sb://localhost:{0};SharedAccessKeyName=RootManageSharedAccessKey;" +
        "SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    private readonly INetwork _network;
    private readonly MsSqlContainer _sqlContainer;
    private readonly IContainer _emulatorContainer;
    private readonly string _configPath;

    private AzureServiceBusEmulatorContainer(
        INetwork network,
        MsSqlContainer sqlContainer,
        IContainer emulatorContainer,
        string configPath)
    {
        _network = network;
        _sqlContainer = sqlContainer;
        _emulatorContainer = emulatorContainer;
        _configPath = configPath;
    }

    /// <summary>
    /// Starts a SQL Server container and the ASB emulator on a shared Docker network.
    /// The emulator is ready when port 5672 becomes available.
    /// </summary>
    public static async Task<AzureServiceBusEmulatorContainer> StartAsync(
        CancellationToken ct = default)
    {
        // Config grants the emulator permission to auto-create entities at runtime.
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

        var configPath = Path.Combine(Path.GetTempPath(), $"asb-emulator-config-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, configJson, ct);

        // Isolated network so the emulator container can resolve the SQL Server by alias.
        var network = new NetworkBuilder().Build();
        await network.CreateAsync(ct);

        // SQL Server — wait strategy built into MsSqlBuilder ensures it is accepting connections
        // before StartAsync returns. Partial failures are cleaned up before propagating.
        MsSqlContainer? sqlContainer = null;
        IContainer? emulatorContainer = null;
        try
        {
            sqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
                .WithNetwork(network)
                .WithNetworkAliases(SqlAlias)
                .WithPassword(SqlPassword)
                .Build();

            await sqlContainer.StartAsync(ct);

            emulatorContainer = new ContainerBuilder(EmulatorImage)
                .WithNetwork(network)
                .WithPortBinding(AmqpPort, true)
                .WithPortBinding(ManagementPort, true)
                .WithEnvironment("ACCEPT_EULA", "Y")
                .WithEnvironment("SQL_SERVER", SqlAlias)
                .WithEnvironment("MSSQL_SA_PASSWORD", SqlPassword)
                .WithBindMount(configPath, "/ServiceBus_Emulator/ConfigFiles/Config.json")
                // Port 5672 opens before SQL Server init completes; the log message is the
                // definitive signal that the emulator is ready to accept AMQP connections.
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilMessageIsLogged("Emulator Service is Successfully Up!"))
                .Build();

            await emulatorContainer.StartAsync(ct);
        }
        catch
        {
            if (emulatorContainer is not null) await emulatorContainer.DisposeAsync();
            if (sqlContainer is not null) await sqlContainer.DisposeAsync();
            await network.DeleteAsync();
            try { File.Delete(configPath); } catch { /* best-effort */ }
            throw;
        }

        return new AzureServiceBusEmulatorContainer(network, sqlContainer, emulatorContainer, configPath);
    }

    /// <summary>Gets the AMQP connection string pointing at the running emulator.</summary>
    public string ConnectionString
    {
        get
        {
            var port = _emulatorContainer.GetMappedPublicPort(AmqpPort);
            return string.Format(ConnectionStringTemplate, port);
        }
    }

    /// <summary>
    /// Gets a connection string for <see cref="Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient"/>
    /// that points at the emulator's HTTP management port (5300).
    /// With <c>UseDevelopmentEmulator=true</c> the SDK derives the management endpoint from the
    /// port in the connection string, so the admin client must use the management port — not the
    /// AMQP port — otherwise HTTP requests land on the AMQP listener and fail.
    /// </summary>
    public string ManagementConnectionString
    {
        get
        {
            var port = _emulatorContainer.GetMappedPublicPort(ManagementPort);
            return string.Format(ConnectionStringTemplate, port);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Attempt every cleanup step so that a failure in one does not leak the others.
        try { await _emulatorContainer.DisposeAsync(); } catch { /* best-effort */ }
        try { await _sqlContainer.DisposeAsync(); } catch { /* best-effort */ }
        try { await _network.DeleteAsync(); } catch { /* best-effort */ }
        try { File.Delete(_configPath); } catch { /* best-effort */ }
    }
}
