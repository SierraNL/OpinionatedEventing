using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Reqnroll;
using Xunit;

namespace OpinionatedEventing.Core.Specs.StepDefinitions;

[Binding]
public sealed class CoreSteps
{
    // ---- shared state ----

    private TestAggregate? _aggregate;
    private ServiceProvider? _provider;

    [AfterScenario]
    public void DisposeProvider()
    {
        _provider?.Dispose();
        _provider = null;
    }

    private Guid _dispatchedCorrelationId;
    private Guid? _dispatchedCausationId;
    private Guid _capturedCorrelationId;
    private Guid? _capturedCausationId;
    private int _handlerInvocationCount;
    private TestCommand? _capturedCommand;

    // ---- domain types ----

    private sealed record TestEvent(Guid Id) : IEvent;
    private sealed record TestCommand(Guid Id) : ICommand;

    private sealed class TestAggregate : AggregateRoot
    {
        public void RaiseEvent(Guid id) => RaiseDomainEvent(new TestEvent(id));
    }

    private sealed class CapturingEventHandler(
        IMessagingContext ctx,
        Action<Guid, Guid?> capture,
        Action increment) : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken)
        {
            capture(ctx.CorrelationId, ctx.CausationId);
            increment();
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingCommandHandler(Action<TestCommand> capture) : ICommandHandler<TestCommand>
    {
        public Task HandleAsync(TestCommand command, CancellationToken cancellationToken)
        {
            capture(command);
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddOpinionatedEventing();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // ---- aggregate scenarios ----

    [Given("an aggregate root")]
    public void GivenAnAggregateRoot() => _aggregate = new TestAggregate();

    [Given("an aggregate root with one domain event raised")]
    public void GivenAnAggregateRootWithOneEvent()
    {
        _aggregate = new TestAggregate();
        _aggregate.RaiseEvent(Guid.NewGuid());
    }

    [When("two domain events are raised")]
    public void WhenTwoDomainEventsAreRaised()
    {
        _aggregate!.RaiseEvent(Guid.NewGuid());
        _aggregate!.RaiseEvent(Guid.NewGuid());
    }

    [When("ClearDomainEvents is called")]
    public void WhenClearDomainEventsIsCalled() =>
        ((IAggregateRoot)_aggregate!).ClearDomainEvents();

    [Then("the DomainEvents collection contains both events in order")]
    public void ThenDomainEventsContainsBothInOrder()
    {
        Assert.Equal(2, _aggregate!.DomainEvents.Count);
        Assert.IsType<TestEvent>(_aggregate.DomainEvents[0]);
        Assert.IsType<TestEvent>(_aggregate.DomainEvents[1]);
    }

    [Then("the DomainEvents collection is empty")]
    public void ThenDomainEventsIsEmpty() =>
        Assert.Empty(_aggregate!.DomainEvents);

    // ---- handler runner scenarios ----

    [Given("a registered event handler that captures IMessagingContext")]
    public void GivenACapturingEventHandler()
    {
        _provider = BuildProvider(s =>
            s.AddScoped<IEventHandler<TestEvent>>(sp =>
                new CapturingEventHandler(
                    sp.GetRequiredService<IMessagingContext>(),
                    (cid, causid) => { _capturedCorrelationId = cid; _capturedCausationId = causid; },
                    () => _handlerInvocationCount++)));
    }

    [Given("two registered event handlers for the same event type")]
    public void GivenTwoEventHandlers()
    {
        _provider = BuildProvider(s =>
        {
            s.AddScoped<IEventHandler<TestEvent>>(sp =>
                new CapturingEventHandler(
                    sp.GetRequiredService<IMessagingContext>(),
                    (_, _) => { },
                    () => _handlerInvocationCount++));
            s.AddScoped<IEventHandler<TestEvent>>(sp =>
                new CapturingEventHandler(
                    sp.GetRequiredService<IMessagingContext>(),
                    (_, _) => { },
                    () => _handlerInvocationCount++));
        });
    }

    [Given("a registered command handler that captures the command")]
    public void GivenACapturingCommandHandler()
    {
        _provider = BuildProvider(s =>
            s.AddScoped<ICommandHandler<TestCommand>>(_ =>
                new CapturingCommandHandler(cmd => { _capturedCommand = cmd; _handlerInvocationCount++; })));
    }

    [When("the handler runner dispatches an event with a known CorrelationId and CausationId")]
    public async Task WhenRunnerDispatchesEventWithCorrelationContext()
    {
        _dispatchedCorrelationId = Guid.NewGuid();
        _dispatchedCausationId = Guid.NewGuid();
        var runner = _provider!.GetRequiredService<IMessageHandlerRunner>();

        await runner.RunAsync(
            typeof(TestEvent).AssemblyQualifiedName!,
            "Event",
            JsonSerializer.Serialize(new TestEvent(Guid.NewGuid())),
            _dispatchedCorrelationId,
            _dispatchedCausationId,
            CancellationToken.None);
    }

    [When("the handler runner dispatches a matching event")]
    public async Task WhenRunnerDispatchesMatchingEvent()
    {
        var runner = _provider!.GetRequiredService<IMessageHandlerRunner>();

        await runner.RunAsync(
            typeof(TestEvent).AssemblyQualifiedName!,
            "Event",
            JsonSerializer.Serialize(new TestEvent(Guid.NewGuid())),
            Guid.NewGuid(),
            null,
            CancellationToken.None);
    }

    [When("the handler runner dispatches a command")]
    public async Task WhenRunnerDispatchesCommand()
    {
        _dispatchedCorrelationId = Guid.NewGuid();
        var runner = _provider!.GetRequiredService<IMessageHandlerRunner>();

        await runner.RunAsync(
            typeof(TestCommand).AssemblyQualifiedName!,
            "Command",
            JsonSerializer.Serialize(new TestCommand(Guid.NewGuid())),
            _dispatchedCorrelationId,
            null,
            CancellationToken.None);
    }

    [Then("the captured CorrelationId matches the dispatched CorrelationId")]
    public void ThenCapturedCorrelationIdMatches() =>
        Assert.Equal(_dispatchedCorrelationId, _capturedCorrelationId);

    [Then("the captured CausationId matches the dispatched CausationId")]
    public void ThenCapturedCausationIdMatches() =>
        Assert.Equal(_dispatchedCausationId, _capturedCausationId);

    [Then("both handlers are invoked")]
    public void ThenBothHandlersAreInvoked() =>
        Assert.Equal(2, _handlerInvocationCount);

    [Then("the command handler is invoked with the correct payload")]
    public void ThenCommandHandlerIsInvokedWithPayload()
    {
        Assert.Equal(1, _handlerInvocationCount);
        Assert.NotNull(_capturedCommand);
    }
}
