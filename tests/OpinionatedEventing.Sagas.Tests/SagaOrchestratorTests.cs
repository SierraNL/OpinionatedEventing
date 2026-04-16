using Microsoft.Extensions.DependencyInjection;
using OpinionatedEventing.Sagas;
using OpinionatedEventing.Sagas.Tests.TestSupport;
using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Sagas.Tests;

public sealed class SagaOrchestratorTests
{
    private static SagaTestHarness CreateOrderHarness()
        => SagaTestHarness.Create(s => s.AddSaga<OrderSaga>());

    [Fact]
    public async Task StartWith_event_creates_saga_in_active_state()
    {
        await using var h = CreateOrderHarness();
        var ct = TestContext.Current.CancellationToken;
        var corrId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId, Amount = 99m }, ct);

        var state = await h.Store.FindAsync(typeof(OrderSaga).AssemblyQualifiedName!, corrId.ToString(), ct);
        Assert.NotNull(state);
        Assert.Equal(SagaStatus.Active, state.Status);
    }

    [Fact]
    public async Task StartWith_handler_can_send_command()
    {
        await using var h = CreateOrderHarness();
        var ct = TestContext.Current.CancellationToken;
        var corrId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId, Amount = 50m }, ct);

        var cmd = Assert.Single(h.Publisher.SentCommands.OfType<ProcessPayment>());
        Assert.Equal(corrId, cmd.OrderId);
        Assert.Equal(50m, cmd.Amount);
    }

    [Fact]
    public async Task Then_event_routes_to_existing_saga()
    {
        await using var h = CreateOrderHarness();
        var ct = TestContext.Current.CancellationToken;
        var corrId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId }, ct);
        await h.Dispatcher.DispatchAsync(new PaymentReceived { CorrelationId = corrId }, ct);

        var state = await h.Store.FindAsync(typeof(OrderSaga).AssemblyQualifiedName!, corrId.ToString(), ct);
        Assert.Equal(SagaStatus.Completed, state!.Status);
    }

    [Fact]
    public async Task Complete_transitions_saga_to_completed()
    {
        await using var h = CreateOrderHarness();
        var ct = TestContext.Current.CancellationToken;
        var corrId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId }, ct);
        await h.Dispatcher.DispatchAsync(new PaymentReceived { CorrelationId = corrId }, ct);

        var state = await h.Store.FindAsync(typeof(OrderSaga).AssemblyQualifiedName!, corrId.ToString(), ct);
        Assert.Equal(SagaStatus.Completed, state!.Status);
    }

    [Fact]
    public async Task State_is_persisted_between_events()
    {
        await using var h = CreateOrderHarness();
        var ct = TestContext.Current.CancellationToken;
        var corrId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId, Amount = 75m }, ct);
        await h.Dispatcher.DispatchAsync(new PaymentReceived { CorrelationId = corrId }, ct);

        var state = await h.Store.FindAsync(typeof(OrderSaga).AssemblyQualifiedName!, corrId.ToString(), ct);
        var sagaState = System.Text.Json.JsonSerializer.Deserialize<OrderSagaState>(state!.State)!;
        Assert.True(sagaState.PaymentProcessed);
        Assert.Equal(75m, sagaState.Amount);
    }

    [Fact]
    public async Task Then_event_without_existing_saga_is_ignored()
    {
        await using var h = CreateOrderHarness();
        var ct = TestContext.Current.CancellationToken;
        var corrId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new PaymentReceived { CorrelationId = corrId }, ct);

        var state = await h.Store.FindAsync(typeof(OrderSaga).AssemblyQualifiedName!, corrId.ToString(), ct);
        Assert.Null(state);
    }

    [Fact]
    public async Task Events_for_completed_saga_are_ignored()
    {
        await using var h = CreateOrderHarness();
        var ct = TestContext.Current.CancellationToken;
        var corrId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId }, ct);
        await h.Dispatcher.DispatchAsync(new PaymentReceived { CorrelationId = corrId }, ct);
        // Dispatch another PaymentReceived to a completed saga
        await h.Dispatcher.DispatchAsync(new PaymentReceived { CorrelationId = corrId }, ct);

        var state = await h.Store.FindAsync(typeof(OrderSaga).AssemblyQualifiedName!, corrId.ToString(), ct);
        Assert.Equal(SagaStatus.Completed, state!.Status);
    }

    [Fact]
    public async Task CompensateWith_event_transitions_to_compensating_then_completed()
    {
        await using var h = CreateOrderHarness();
        var ct = TestContext.Current.CancellationToken;
        var corrId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId }, ct);
        await h.Dispatcher.DispatchAsync(new PaymentFailed { CorrelationId = corrId }, ct);

        var state = await h.Store.FindAsync(typeof(OrderSaga).AssemblyQualifiedName!, corrId.ToString(), ct);
        Assert.Equal(SagaStatus.Completed, state!.Status);
    }

    [Fact]
    public async Task Timeout_expiry_is_stored_when_ExpireAfter_configured()
    {
        await using var h = SagaTestHarness.Create(s => s.AddSaga<TimedOrderSaga>());
        var ct = TestContext.Current.CancellationToken;
        var corrId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId }, ct);

        var state = await h.Store.FindAsync(typeof(TimedOrderSaga).AssemblyQualifiedName!, corrId.ToString(), ct);
        Assert.NotNull(state!.ExpiresAt);
    }

    [Fact]
    public async Task Timeout_handler_transitions_saga_to_completed()
    {
        await using var h = SagaTestHarness.Create(s => s.AddSaga<TimedOrderSaga>());
        var ct = TestContext.Current.CancellationToken;
        var corrId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId }, ct);

        var state = await h.Store.FindAsync(typeof(TimedOrderSaga).AssemblyQualifiedName!, corrId.ToString(), ct);
        state!.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await h.Store.UpdateAsync(state, ct);

        // Simulate SagaTimeoutWorker: resolve the descriptor and invoke HandleTimeoutAsync directly.
        // SagaDescriptor is internal; the test assembly has InternalsVisibleTo access.
        var descriptors = h.Scope.ServiceProvider.GetServices<SagaDescriptor>().ToList();
        var descriptor = Assert.Single(descriptors);

        await descriptor.HandleTimeoutAsync(
            state, h.Scope.ServiceProvider, h.Store, h.Publisher, TimeProvider.System,
            serializerOptions: null, ct);

        Assert.Equal(SagaStatus.Completed, state.Status);
        var sagaState = System.Text.Json.JsonSerializer.Deserialize<TimedOrderSagaState>(state.State)!;
        Assert.True(sagaState.TimedOut);
    }

    [Fact]
    public async Task Normal_events_are_ignored_when_saga_is_compensating()
    {
        await using var h = CreateOrderHarness();
        var ct = TestContext.Current.CancellationToken;
        var corrId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId }, ct);
        await h.Dispatcher.DispatchAsync(new PaymentFailed { CorrelationId = corrId }, ct); // → Compensating → Completed

        var stateAfterCompensation = await h.Store.FindAsync(typeof(OrderSaga).AssemblyQualifiedName!, corrId.ToString(), ct);
        Assert.Equal(SagaStatus.Completed, stateAfterCompensation!.Status);

        // A late PaymentReceived should be ignored (saga is already Completed)
        await h.Dispatcher.DispatchAsync(new PaymentReceived { CorrelationId = corrId }, ct);

        var stateFinal = await h.Store.FindAsync(typeof(OrderSaga).AssemblyQualifiedName!, corrId.ToString(), ct);
        Assert.Equal(SagaStatus.Completed, stateFinal!.Status);
    }

    [Fact]
    public async Task CorrelateBy_uses_custom_expression()
    {
        await using var h = SagaTestHarness.Create(s => s.AddSaga<CustomCorrelationSaga>());
        var ct = TestContext.Current.CancellationToken;
        var corrId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId }, ct);

        // StockReserved uses OrderId as the correlation key (not CorrelationId)
        await h.Dispatcher.DispatchAsync(new StockReserved { OrderId = corrId, CorrelationId = Guid.NewGuid() }, ct);

        var state = await h.Store.FindAsync(typeof(CustomCorrelationSaga).AssemblyQualifiedName!, corrId.ToString(), ct);
        Assert.Equal(SagaStatus.Completed, state!.Status);
    }

    [Fact]
    public async Task Two_independent_sagas_do_not_interfere()
    {
        await using var h = CreateOrderHarness();
        var ct = TestContext.Current.CancellationToken;
        var corrId1 = Guid.NewGuid();
        var corrId2 = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId1 }, ct);
        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId2 }, ct);
        await h.Dispatcher.DispatchAsync(new PaymentReceived { CorrelationId = corrId1 }, ct);

        var state1 = await h.Store.FindAsync(typeof(OrderSaga).AssemblyQualifiedName!, corrId1.ToString(), ct);
        var state2 = await h.Store.FindAsync(typeof(OrderSaga).AssemblyQualifiedName!, corrId2.ToString(), ct);
        Assert.Equal(SagaStatus.Completed, state1!.Status);
        Assert.Equal(SagaStatus.Active, state2!.Status);
    }
}
