#nullable enable

namespace OpinionatedEventing.Outbox;

/// <summary>
/// Optional guard that validates an ambient database transaction is active before a message is
/// written to the outbox. Implement and register this interface (e.g. in
/// <c>OpinionatedEventing.EntityFramework</c>) to enforce that <see cref="IPublisher"/> is only
/// called within an active transaction.
/// </summary>
public interface IOutboxTransactionGuard
{
    /// <summary>
    /// Validates that an ambient transaction is currently active.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="IPublisher"/> is called outside an active transaction.
    /// </exception>
    void EnsureTransaction();
}
