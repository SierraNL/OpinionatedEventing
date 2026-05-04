#nullable enable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace OpinionatedEventing.RabbitMQ;

/// <summary>
/// Hosted service that establishes the RabbitMQ connection asynchronously during host startup.
/// Stores the result in <see cref="RabbitMqConnectionHolder"/> so that
/// <see cref="RabbitMQTransport"/>, <see cref="RabbitMQTopologyInitializer"/>, and
/// <see cref="RabbitMQConsumerWorker"/> can await it without blocking.
/// </summary>
internal sealed class RabbitMqConnectionInitializer : IHostedService, IAsyncDisposable
{
    private readonly RabbitMqConnectionHolder _holder;
    private readonly IOptions<RabbitMQOptions> _options;
    private readonly IConfiguration? _config;
    private readonly ILogger<RabbitMqConnectionInitializer> _logger;

    private IConnection? _connection;

    /// <summary>Initialises a new <see cref="RabbitMqConnectionInitializer"/>.</summary>
    public RabbitMqConnectionInitializer(
        RabbitMqConnectionHolder holder,
        IOptions<RabbitMQOptions> options,
        ILogger<RabbitMqConnectionInitializer> logger,
        IConfiguration? config = null)
    {
        _holder = holder;
        _options = options;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        var connectionString = ResolveConnectionString(opts, _config);
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };

        _logger.LogInformation("Establishing RabbitMQ connection to '{Host}'...", factory.HostName);

        try
        {
            _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            _holder.SetConnection(_connection);
            _logger.LogInformation("RabbitMQ connection established.");
        }
        catch (Exception ex)
        {
            _holder.SetException(ex);
            _logger.LogError(ex, "Failed to establish RabbitMQ connection.");
            throw;
        }
    }

    /// <inheritdoc/>
    // Intentional no-op: the connection must stay open until all consumers and the topology
    // initializer have completed their own StopAsync teardown. Disposal happens in DisposeAsync.
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);
    }

    internal static string ResolveConnectionString(RabbitMQOptions opts, IConfiguration? config)
    {
        if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
            return opts.ConnectionString;

        var key = $"ConnectionStrings:{opts.AspireConnectionStringName}";
        var aspireConnectionString = config?[key];
        if (!string.IsNullOrWhiteSpace(aspireConnectionString))
            return aspireConnectionString;

        throw new InvalidOperationException(
            $"RabbitMQOptions requires either ConnectionString to be set, " +
            $"or a '{key}' entry in IConfiguration (Aspire service discovery).");
    }
}
