#nullable enable

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpinionatedEventing.Sagas;
using OpinionatedEventing.Testing;
using Reqnroll;

namespace OpinionatedEventing.Sagas.Specs.StepDefinitions;

[Binding]
public sealed class SagasSteps : IAsyncDisposable
{
    private Guid _correlationId;
    private ISagaDispatcher? _dispatcher;
    private InMemorySagaStateStore? _store;
    private ServiceProvider? _root;
    private IServiceScope? _scope;

    // --- Given ---

    [Given("the order saga is registered")]
    public void GivenOrderSagaIsRegistered()
    {
        _correlationId = Guid.NewGuid();
        (_dispatcher, _store, _root, _scope) = BuildHarness(svc =>
            svc.AddSaga<SpecsOrderSaga>());
    }

    [Given("an OrderPlaced event has been dispatched")]
    public async Task GivenOrderPlacedEventDispatched()
    {
        await _dispatcher!.DispatchAsync(
            new SpecsOrderPlaced { CorrelationId = _correlationId, Amount = 100m });
    }

    // --- When ---

    [When("an OrderPlaced event is dispatched")]
    public async Task WhenOrderPlacedEventDispatched()
    {
        await _dispatcher!.DispatchAsync(
            new SpecsOrderPlaced { CorrelationId = _correlationId, Amount = 100m });
    }

    [When("a PaymentReceived event is dispatched")]
    public async Task WhenPaymentReceivedEventDispatched()
    {
        await _dispatcher!.DispatchAsync(
            new SpecsPaymentReceived { CorrelationId = _correlationId });
    }

    [When("a PaymentFailed event is dispatched")]
    public async Task WhenPaymentFailedEventDispatched()
    {
        await _dispatcher!.DispatchAsync(
            new SpecsPaymentFailed { CorrelationId = _correlationId });
    }

    // --- Then ---

    [Then("a saga instance exists in the store")]
    public void ThenSagaInstanceExistsInStore()
    {
        Xunit.Assert.Single(_store!.States);
    }

    [Then("the saga status is Active")]
    public void ThenSagaStatusIsActive()
    {
        Xunit.Assert.Equal(SagaStatus.Active, _store!.States[0].Status);
    }

    [Then("the saga status is Completed")]
    public void ThenSagaStatusIsCompleted()
    {
        Xunit.Assert.Equal(SagaStatus.Completed, _store!.States[0].Status);
    }

    [Then("the saga instance state shows payment was processed")]
    public void ThenSagaStateShowsPaymentProcessed()
    {
        // Saga serializes with default STJ options (PascalCase); deserialize with same defaults.
        var state = JsonSerializer.Deserialize<SpecsOrderSagaState>(_store!.States[0].State);
        Xunit.Assert.True(state!.PaymentProcessed);
    }

    [Then("the saga instance state shows payment was not processed")]
    public void ThenSagaStateShowsPaymentNotProcessed()
    {
        var state = JsonSerializer.Deserialize<SpecsOrderSagaState>(_store!.States[0].State);
        Xunit.Assert.False(state!.PaymentProcessed);
    }

    // --- IAsyncDisposable ---

    public async ValueTask DisposeAsync()
    {
        _scope?.Dispose();
        if (_root is not null) await _root.DisposeAsync();
    }

    // --- private helpers ---

    private static (ISagaDispatcher, InMemorySagaStateStore, ServiceProvider, IServiceScope) BuildHarness(
        Action<IServiceCollection> configure)
    {
        var store = new InMemorySagaStateStore();
        var publisher = new FakePublisher();

        var services = new ServiceCollection();
        services.AddSingleton<ISagaStateStore>(store);
        services.AddSingleton<IPublisher>(publisher);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(
            typeof(ILogger<SagaTimeoutWorker>),
            NullLogger<SagaTimeoutWorker>.Instance);
        services.AddOpinionatedEventingSagas();
        configure(services);

        var root = services.BuildServiceProvider(validateScopes: true);
        var scope = root.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ISagaDispatcher>();

        return (dispatcher, store, root, scope);
    }

    // --- inner saga types ---

    private sealed class SpecsOrderSagaState
    {
        public Guid OrderId { get; set; }
        public decimal Amount { get; set; }
        public bool PaymentProcessed { get; set; }
    }

    private sealed record SpecsOrderPlaced : IEvent
    {
        public Guid CorrelationId { get; init; }
        public decimal Amount { get; init; }
    }

    private sealed record SpecsPaymentReceived : IEvent
    {
        public Guid CorrelationId { get; init; }
    }

    private sealed record SpecsPaymentFailed : IEvent
    {
        public Guid CorrelationId { get; init; }
    }

    private sealed record SpecsProcessPayment : ICommand
    {
        public Guid OrderId { get; init; }
        public decimal Amount { get; init; }
    }

    private sealed class SpecsOrderSaga : SagaOrchestrator<SpecsOrderSagaState>
    {
        protected override void Configure(ISagaBuilder<SpecsOrderSagaState> builder)
        {
            builder
                .StartWith<SpecsOrderPlaced>(OnOrderPlaced)
                .Then<SpecsPaymentReceived>(OnPaymentReceived)
                .CompensateWith<SpecsPaymentFailed>(OnPaymentFailed);
        }

        private Task OnOrderPlaced(SpecsOrderPlaced evt, SpecsOrderSagaState state, ISagaContext ctx)
        {
            state.OrderId = evt.CorrelationId;
            state.Amount = evt.Amount;
            return ctx.SendCommandAsync(new SpecsProcessPayment { OrderId = state.OrderId, Amount = state.Amount });
        }

        private Task OnPaymentReceived(SpecsPaymentReceived _, SpecsOrderSagaState state, ISagaContext ctx)
        {
            state.PaymentProcessed = true;
            ctx.Complete();
            return Task.CompletedTask;
        }

        private Task OnPaymentFailed(SpecsPaymentFailed _, SpecsOrderSagaState state, ISagaContext ctx)
        {
            ctx.Complete();
            return Task.CompletedTask;
        }
    }
}
