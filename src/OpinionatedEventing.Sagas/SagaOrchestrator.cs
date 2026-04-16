namespace OpinionatedEventing.Sagas;

/// <summary>
/// Abstract base class for saga orchestrators.
/// Override <see cref="Configure"/> to register event handlers, compensation steps, and timeouts.
/// </summary>
/// <typeparam name="TSagaState">
/// The POCO that holds the saga's durable state. Must have a public parameterless constructor
/// and be JSON-serialisable.
/// </typeparam>
/// <example>
/// <code>
/// public class OrderSaga : SagaOrchestrator&lt;OrderSagaState&gt;
/// {
///     protected override void Configure(ISagaBuilder&lt;OrderSagaState&gt; builder)
///     {
///         builder
///             .StartWith&lt;OrderPlaced&gt;(OnOrderPlaced)
///             .Then&lt;PaymentReceived&gt;(OnPaymentReceived)
///             .CompensateWith&lt;PaymentFailed&gt;(OnPaymentFailed)
///             .ExpireAfter(TimeSpan.FromMinutes(30));
///     }
///
///     private Task OnOrderPlaced(OrderPlaced evt, OrderSagaState state, ISagaContext ctx)
///     {
///         state.OrderId = evt.OrderId;
///         return ctx.SendCommandAsync(new ProcessPayment(evt.OrderId, evt.Amount));
///     }
/// }
/// </code>
/// </example>
public abstract class SagaOrchestrator<TSagaState> where TSagaState : class, new()
{
    private SagaDefinition<TSagaState>? _definition;

    /// <summary>
    /// Configures the saga's event handlers, compensation steps, timeouts, and correlation strategy.
    /// Called once per orchestrator instance when the first event is dispatched.
    /// </summary>
    /// <param name="builder">The fluent builder to configure.</param>
    protected abstract void Configure(ISagaBuilder<TSagaState> builder);

    internal SagaDefinition<TSagaState> GetDefinition()
    {
        if (_definition is not null) return _definition;
        var builder = new SagaBuilder<TSagaState>();
        Configure(builder);
        return _definition = builder.Build();
    }
}
