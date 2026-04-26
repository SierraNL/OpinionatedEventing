namespace OpinionatedEventing;

/// <summary>
/// Handles commands of type <typeparamref name="TCommand"/>.
/// Exactly one registration per command type is allowed — duplicates throw at startup.
/// </summary>
/// <typeparam name="TCommand">The command type to handle.</typeparam>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    /// <summary>Handles the given <paramref name="command"/>.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task HandleAsync(TCommand command, CancellationToken cancellationToken);
}
