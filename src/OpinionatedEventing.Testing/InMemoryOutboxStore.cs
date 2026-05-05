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
    private readonly TimeProvider _timeProvider;

    /// <summary>Initialises a new <see cref="InMemoryOutboxStore"/>.</summary>
    /// <param name="timeProvider">
    /// Time source used for <c>ProcessedAt</c>, <c>FailedAt</c>, and pending-message filtering.
    /// Defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.
    /// Pass a <see cref="FakeTimeProvider"/> to control time in tests.
    /// </param>
    public InMemoryOutboxStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets a snapshot of all messages currently in the store, regardless of status.</summary>
    public IReadOnlyList<OutboxMessage> Messages => _messages.Values.ToList();

    /// <summary>Gets a snapshot of messages that have not yet been processed or dead-lettered.</summary>
    public IReadOnlyList<OutboxMessage> PendingMessages => _messages.Values
        .Where(m => m.ProcessedAt is null && m.FailedAt is null)
        .ToList();

    /// <summary>Gets a snapshot of messages that have been successfully processed.</summary>
    public IReadOnlyList<OutboxMessage> ProcessedMessages => _messages.Values
        .Where(m => m.ProcessedAt is not null)
        .ToList();

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
        DateTimeOffset now = _timeProvider.GetUtcNow();
        var pending = _messages.Values
            .Where(m => m.ProcessedAt is null && m.FailedAt is null &&
                        (m.NextAttemptAt is null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToList();

        return Task.FromResult<IReadOnlyList<OutboxMessage>>(pending);
    }

    /// <inheritdoc/>
    public Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(id, out var message))
            message.ProcessedAt = _timeProvider.GetUtcNow();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(id, out var message))
        {
            message.FailedAt = _timeProvider.GetUtcNow();
            message.Error = error;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task IncrementAttemptAsync(Guid id, string error, DateTimeOffset? nextAttemptAt, CancellationToken cancellationToken = default)
    {
        if (_messages.TryGetValue(id, out var message))
        {
            message.AttemptCount++;
            message.Error = error;
            message.NextAttemptAt = nextAttemptAt;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> DeleteProcessedAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        var toDelete = _messages.Values
            .Where(m => m.ProcessedAt is not null && m.ProcessedAt < cutoff)
            .Select(m => m.Id)
            .ToList();

        foreach (Guid id in toDelete)
            _messages.TryRemove(id, out _);

        return Task.FromResult(toDelete.Count);
    }

    /// <inheritdoc/>
    public Task<int> DeleteFailedAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        var toDelete = _messages.Values
            .Where(m => m.FailedAt is not null && m.FailedAt < cutoff)
            .Select(m => m.Id)
            .ToList();

        foreach (Guid id in toDelete)
            _messages.TryRemove(id, out _);

        return Task.FromResult(toDelete.Count);
    }
}
