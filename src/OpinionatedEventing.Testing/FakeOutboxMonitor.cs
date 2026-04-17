#nullable enable

using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.Testing;

/// <summary>
/// Configurable in-memory implementation of <see cref="IOutboxMonitor"/> for use in unit tests.
/// Not for production use.
/// </summary>
public sealed class FakeOutboxMonitor : IOutboxMonitor
{
    /// <summary>Gets or sets the value returned by <see cref="GetPendingCountAsync"/>.</summary>
    public int PendingCount { get; set; }

    /// <summary>Gets or sets the value returned by <see cref="GetDeadLetterCountAsync"/>.</summary>
    public int DeadLetterCount { get; set; }

    /// <inheritdoc/>
    public Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(PendingCount);

    /// <inheritdoc/>
    public Task<int> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(DeadLetterCount);
}
