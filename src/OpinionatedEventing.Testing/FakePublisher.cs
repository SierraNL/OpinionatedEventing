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
        _sentCommands.Add(command!);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task PublishEventAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        _publishedEvents.Add(@event!);
        return Task.CompletedTask;
    }
}
