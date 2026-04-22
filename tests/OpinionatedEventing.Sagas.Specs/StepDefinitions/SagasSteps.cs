#nullable enable

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpinionatedEventing.Sagas;
using OpinionatedEventing.Sagas.Options;
using OpinionatedEventing.Testing;
using Reqnroll;

namespace OpinionatedEventing.Sagas.Specs.StepDefinitions;

[Binding]
public sealed class SagasSteps : IAsyncDisposable
{
    private Guid _correlationId;
    private ISagaDispatcher? _dispatcher;
    private InMemorySagaStateStore? _store;
    private FakePublisher? _publisher;
    private ServiceProvider? _root;
    private IServiceScope? _scope;
    private SpecsTimeoutSignal? _timeoutSignal;

    // --- Given ---

    [Given("the order saga is registered")]
    public void GivenOrderSagaIsRegistered()
    {
        _correlationId = Guid.NewGuid();
        (_dispatcher, _store, _publisher, _root, _scope) = BuildHarness(svc =>
            svc.AddSaga<SpecsOrderSaga>());
    }

    [Given("the notification participant is registered")]
    public void GivenNotificationParticipantIsRegistered()
    {
        _correlationId = Guid.NewGuid();
        (_dispatcher, _store, _publisher, _root, _scope) = BuildHarness(svc =>
            svc.AddSagaParticipant<SpecsNotificationParticipant>());
    }

    [Given("the order saga with timeout is registered")]
    public void GivenOrderSagaWithTimeoutIsRegistered()
    {
        _correlationId = Guid.NewGuid();
        _timeoutSignal = new SpecsTimeoutSignal();
        (_dispatcher, _store, _publisher, _root, _scope) = BuildHarness(
            svc =>
            {
                svc.AddSingleton(_timeoutSignal);
                svc.AddSaga<SpecsTimeoutOrderSaga>();
            },
            timeoutInterval: TimeSpan.FromMilliseconds(10));
    }

    [Given("the order saga with custom correlation is registered")]
    public void GivenOrderSagaWithCustomCorrelationIsRegistered()
    {
        _correlationId = Guid.NewGuid();
        (_dispatcher, _store, _publisher, _root, _scope) = BuildHarness(svc =>
            svc.AddSaga<SpecsCustomCorrelationSaga>());
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

    [When("an OrderShipped event is dispatched")]
    public async Task WhenOrderShippedEventDispatched()
    {
        await _dispatcher!.DispatchAsync(
            new SpecsOrderShipped { CorrelationId = _correlationId });
    }

    [When("the saga timeout worker processes expired sagas")]
    public async Task WhenSagaTimeoutWorkerProcessesExpiredSagas()
    {
        // Force the saga state to appear expired so the worker picks it up immediately.
        var existingState = _store!.States.FirstOrDefault();
        if (existingState is not null)
        {
            existingState.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
            await _store.UpdateAsync(existingState);
        }

        var worker = _root!.GetServices<IHostedService>().OfType<SagaTimeoutWorker>().First();
        await worker.StartAsync(CancellationToken.None);

        // Poll until the timeout handler fires or 5 s elapses.
        for (var i = 0; i < 500 && _timeoutSignal?.Fired != true; i++)
            await Task.Delay(10);

        // StopAsync cancels the internal _stoppingCts that ExecuteAsync uses.
        await worker.StopAsync(CancellationToken.None);
    }

    [When("an OrderPlaced event is dispatched with custom correlation key {string}")]
    public async Task WhenOrderPlacedWithCustomCorrelation(string key)
    {
        await _dispatcher!.DispatchAsync(new SpecsCustomOrderPlaced { CustomKey = key });
    }

    [When("a PaymentReceived event is dispatched with custom correlation key {string}")]
    public async Task WhenPaymentReceivedWithCustomCorrelation(string key)
    {
        await _dispatcher!.DispatchAsync(new SpecsCustomPaymentReceived { CustomKey = key });
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

    [Then("the notification participant command was published")]
    public void ThenNotificationParticipantCommandWasPublished()
    {
        Xunit.Assert.Single(_publisher!.SentCommands);
        Xunit.Assert.IsType<SpecsSendNotification>(_publisher.SentCommands[0]);
    }

    [Then("the saga timeout handler was invoked")]
    public void ThenSagaTimeoutHandlerWasInvoked()
    {
        Xunit.Assert.True(_timeoutSignal?.Fired);
    }

    // --- IAsyncDisposable ---

    public async ValueTask DisposeAsync()
    {
        _scope?.Dispose();
        if (_root is not null) await _root.DisposeAsync();
    }

    // --- private helpers ---

    private (ISagaDispatcher, InMemorySagaStateStore, FakePublisher, ServiceProvider, IServiceScope) BuildHarness(
        Action<IServiceCollection> configure,
        TimeSpan? timeoutInterval = null)
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
        services.AddOpinionatedEventingSagas(opts =>
        {
            if (timeoutInterval.HasValue)
                opts.TimeoutCheckInterval = timeoutInterval.Value;
        });
        configure(services);

        var root = services.BuildServiceProvider(validateScopes: true);
        var scope = root.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ISagaDispatcher>();

        return (dispatcher, store, publisher, root, scope);
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

    // --- choreography participant types ---

    private sealed record SpecsOrderShipped : IEvent
    {
        public Guid CorrelationId { get; init; }
    }

    private sealed record SpecsSendNotification : ICommand
    {
        public Guid OrderId { get; init; }
    }

    private sealed class SpecsNotificationParticipant : ISagaParticipant<SpecsOrderShipped>
    {
        public Task HandleAsync(SpecsOrderShipped @event, ISagaContext ctx, CancellationToken cancellationToken)
            => ctx.SendCommandAsync(new SpecsSendNotification { OrderId = @event.CorrelationId }, cancellationToken);
    }

    // --- timeout saga types ---

    private sealed class SpecsTimeoutSignal
    {
        public bool Fired { get; set; }
    }

    private sealed class SpecsTimeoutOrderSagaState
    {
        public bool TimedOut { get; set; }
    }

    private sealed class SpecsTimeoutOrderSaga : SagaOrchestrator<SpecsTimeoutOrderSagaState>
    {
        private readonly SpecsTimeoutSignal _signal;

        public SpecsTimeoutOrderSaga(SpecsTimeoutSignal signal) => _signal = signal;

        protected override void Configure(ISagaBuilder<SpecsTimeoutOrderSagaState> builder)
        {
            builder
                .StartWith<SpecsOrderPlaced>((_, _, _) => Task.CompletedTask)
                .ExpireAfter(TimeSpan.FromMilliseconds(1))
                .OnTimeout((state, ctx) =>
                {
                    state.TimedOut = true;
                    _signal.Fired = true;
                    ctx.Complete();
                    return Task.CompletedTask;
                });
        }
    }

    // --- custom correlation saga types ---

    private sealed record SpecsCustomOrderPlaced : IEvent
    {
        public string CustomKey { get; init; } = string.Empty;
    }

    private sealed record SpecsCustomPaymentReceived : IEvent
    {
        public string CustomKey { get; init; } = string.Empty;
    }

    private sealed class SpecsCustomCorrelationSagaState
    {
        public bool PaymentProcessed { get; set; }
    }

    private sealed class SpecsCustomCorrelationSaga : SagaOrchestrator<SpecsCustomCorrelationSagaState>
    {
        protected override void Configure(ISagaBuilder<SpecsCustomCorrelationSagaState> builder)
        {
            builder
                .StartWith<SpecsCustomOrderPlaced>((_, _, _) => Task.CompletedTask)
                .Then<SpecsCustomPaymentReceived>((_, state, ctx) =>
                {
                    state.PaymentProcessed = true;
                    ctx.Complete();
                    return Task.CompletedTask;
                })
                .CorrelateBy<SpecsCustomOrderPlaced>(e => e.CustomKey)
                .CorrelateBy<SpecsCustomPaymentReceived>(e => e.CustomKey);
        }
    }
}
