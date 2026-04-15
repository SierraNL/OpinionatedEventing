#nullable enable

using System.Collections.Concurrent;
using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.Testing;

/// <summary>
/// In-memory implementation of <see cref="IOutboxStore"/> for use in unit tests.
/// Thread-safe. Not for production use.
/// </summary>
public sealed class InMemoryOutboxStore : IOutboxStore
{
    private readonly ConcurrentDictionary<Guid, OutboxMessage> _messages = new();

    /// <summary>Gets a snapshot of all messages currently in the store, regardless of status.</summary>
    public IReadOnlyList<OutboxMessage> Messages => _messages.Values.ToList();

    /// <inheritdoc/>
    public Task SaveAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        _messages[message.Id] = message;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var pending = _messages.Values
            .Where(m => m.ProcessedAt is null && m.FailedAt is null)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToList();

        return Task.FromResult<IReadOnlyList<OutboxMessage>>(pending);
    }

    /// <inheritdoc/>
    public Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(id, out var message))
            message.ProcessedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(id, out var message))
        {
            message.FailedAt = DateTimeOffset.UtcNow;
            message.Error = error;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task IncrementAttemptAsync(Guid id, string error, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(id, out var message))
        {
            message.AttemptCount++;
            message.Error = error;
        }
        return Task.CompletedTask;
    }
}
