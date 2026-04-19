#nullable enable

namespace OpinionatedEventing.Testing;

/// <summary>
/// In-memory implementation of <see cref="IPublisher"/> for use in unit tests.
/// Captures sent commands and published events without any broker interaction.
/// Not for production use.
/// </summary>
public sealed class FakePublisher : IPublisher
{
    private readonly List<object> _sentCommands = new();
    private readonly List<object> _publishedEvents = new();

    /// <summary>Gets all commands sent via <see cref="SendCommandAsync{TCommand}"/>, in order.</summary>
    public IReadOnlyList<object> SentCommands => _sentCommands;

    /// <summary>Gets all events published via <see cref="PublishEventAsync{TEvent}"/>, in order.</summary>
    public IReadOnlyList<object> PublishedEvents => _publishedEvents;

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
}
