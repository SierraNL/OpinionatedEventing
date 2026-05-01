#nullable enable

using RabbitMQ.Client;

namespace OpinionatedEventing.RabbitMQ;

/// <summary>
/// Singleton that holds the RabbitMQ connection once established by
/// <see cref="RabbitMqConnectionInitializer"/>. Consumers await
/// <see cref="GetConnectionAsync"/> rather than blocking the DI thread.
/// </summary>
internal sealed class RabbitMqConnectionHolder
{
    private readonly TaskCompletionSource<IConnection> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Returns the connection once it has been established, or throws if
    /// initialization failed. Respects <paramref name="ct"/>.
    /// </summary>
    public Task<IConnection> GetConnectionAsync(CancellationToken ct)
        => _tcs.Task.WaitAsync(ct);

    /// <summary>
    /// Returns the connection if already established; otherwise <see langword="null"/>.
    /// Safe to call without awaiting (used by the health check).
    /// </summary>
    public IConnection? TryGetConnection()
        => _tcs.Task.IsCompletedSuccessfully ? _tcs.Task.Result : null;

    internal void SetConnection(IConnection connection) => _tcs.TrySetResult(connection);

    internal void SetException(Exception exception) => _tcs.TrySetException(exception);
}
