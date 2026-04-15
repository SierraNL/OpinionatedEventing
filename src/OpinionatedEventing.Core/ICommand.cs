namespace OpinionatedEventing;

/// <summary>
/// Marker interface for commands.
/// Commands represent an instruction to perform an action.
/// Exactly one <see cref="ICommandHandler{TCommand}"/> must be registered per command type.
/// Implementations must be immutable — use <c>record</c> types.
/// </summary>
public interface ICommand { }
