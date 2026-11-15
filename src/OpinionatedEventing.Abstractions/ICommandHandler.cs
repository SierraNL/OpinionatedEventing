namespace OpinionatedEventing;

/// <summary>
/// Handles commands of type <typeparamref name="TCommand"/>.
/// Exactly one registration per command type is allowed — duplicates throw at startup.
/// </summary>
/// <remarks>Delivery is at-least-once, so the handler must be idempotent.</remarks>
/// <example>
/// <code>
/// public class PlaceOrderHandler : ICommandHandler&lt;PlaceOrder&gt;
/// {
///     public Task HandleAsync(PlaceOrder command, CancellationToken cancellationToken) =&gt; Task.CompletedTask;
/// }
/// </code>
/// </example>
/// <typeparam name="TCommand">The command type to handle.</typeparam>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    /// <summary>Handles the given <paramref name="command"/>.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    Task HandleAsync(TCommand command, CancellationToken cancellationToken);
}
