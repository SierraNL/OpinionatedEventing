#nullable enable

using OpinionatedEventing.Sagas;

namespace OpinionatedEventing.Testing;

/// <summary>
/// In-memory implementation of <see cref="ISagaContext"/> for use in unit tests.
/// Captures sent commands and published events without any broker interaction.
/// Not for production use.
/// </summary>
public sealed class FakeSagaContext : ISagaContext
{
    private readonly List<object> _sentCommands = new();
    private readonly List<object> _publishedEvents = new();

    /// <summary>Gets or sets the correlation identifier returned by this context. Defaults to a random <see cref="Guid"/>.</summary>
    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    /// <summary>Gets all commands sent via <see cref="SendCommandAsync{TCommand}"/>, in order.</summary>
    public IReadOnlyList<object> SentCommands => _sentCommands;

    /// <summary>Gets all events published via <see cref="PublishEventAsync{TEvent}"/>, in order.</summary>
    public IReadOnlyList<object> PublishedEvents => _publishedEvents;

    /// <summary>Gets a value indicating whether <see cref="Complete"/> has been called.</summary>
    public bool IsCompleted { get; private set; }

    /// <inheritdoc/>
    public Task SendCommandAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        // TCommand : ICommand does not imply notnull without a class/notnull constraint; safe because
        // ICommand is an interface (reference type only) and callers always pass a non-null instance.
        _sentCommands.Add(command!);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task PublishEventAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        // Same reasoning as SendCommandAsync — TEvent is always a non-null reference type at the call site.
        _publishedEvents.Add(@event!);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Complete() => IsCompleted = true;
}
