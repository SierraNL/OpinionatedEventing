#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpinionatedEventing;
using OpinionatedEventing.Options;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Testing;
using Reqnroll;
using Xunit;

namespace OpinionatedEventing.Outbox.Specs.StepDefinitions;

[Binding]
public sealed class OutboxSteps
{
    // ---- shared state ----

    private Guid _correlationId = Guid.NewGuid();
    private readonly InMemoryOutboxStore _store = new();
    private readonly FakeTransport _transport = new();
    private IPublisher _publisher = null!;
    private OutboxMessage? _savedMessage;
    private Exception? _thrownException;
    private int _maxAttempts = 5;

    // ---- Given ----

    [Given("a messaging context with a known correlation ID")]
    public void GivenMessagingContextWithKnownCorrelationId()
    {
        _correlationId = Guid.NewGuid();
        _publisher = BuildPublisher(_correlationId, null, []);
    }

    [Given("a pending outbox message exists in the store")]
    public async Task GivenPendingOutboxMessageExistsInStore()
    {
        _savedMessage = MakePendingMessage();
        await _store.SaveAsync(_savedMessage);
    }

    [Given(@"a pending outbox message with (\d+) failed attempts exists in the store")]
    public async Task GivenPendingOutboxMessageWithFailedAttemptsExistsInStore(int attempts)
    {
        _savedMessage = MakePendingMessage(attemptCount: attempts);
        await _store.SaveAsync(_savedMessage);
    }

    [Given("the transport will fail on the next attempt")]
    public void GivenTransportWillFailOnNextAttempt() => _transport.FailNextCount = 1;

    [Given("the transport always fails")]
    public void GivenTransportAlwaysFails() => _transport.FailAlways = true;

    [Given(@"the max attempts is configured to (\d+)")]
    public void GivenMaxAttemptsConfiguredTo(int maxAttempts) => _maxAttempts = maxAttempts;

    [Given("a transaction guard that always rejects")]
    public void GivenTransactionGuardThatAlwaysRejects()
    {
        _correlationId = Guid.NewGuid();
        _publisher = BuildPublisher(_correlationId, null, [new ThrowingTransactionGuard()]);
    }

    // ---- When ----

    [When("an event is published via IPublisher")]
    public async Task WhenEventPublishedViaPublisher()
    {
        try
        {
            await _publisher.PublishEventAsync(new TestEvent(Guid.NewGuid()));
        }
        catch (Exception ex)
        {
            _thrownException = ex;
        }
    }

    [When("a command is sent via IPublisher")]
    public async Task WhenCommandSentViaPublisher()
    {
        await _publisher.SendCommandAsync(new TestCommand(Guid.NewGuid()));
    }

    [When("the dispatcher worker processes the batch")]
    public async Task WhenDispatcherWorkerProcessesBatch()
    {
        await RunWorkerForOnePassAsync();
    }

    // ---- Then ----

    [Then(@"one outbox message with kind ""(.*)"" is saved to the store")]
    public void ThenOutboxMessageWithKindSaved(string kind)
    {
        Assert.Single(_store.Messages);
        Assert.Equal(Enum.Parse<MessageKind>(kind), _store.Messages[0].MessageKind);
    }

    [Then("the outbox message carries the correlation ID")]
    public void ThenOutboxMessageCarriesCorrelationId()
    {
        Assert.Equal(_correlationId, _store.Messages[0].CorrelationId);
    }

    [Then("the message is forwarded to the transport")]
    public void ThenMessageForwardedToTransport()
    {
        Assert.NotNull(_savedMessage); // guard: ! below is safe
        Assert.Contains(_savedMessage!.Id, _transport.SentMessageIds);
    }

    [Then("the message is marked as processed in the store")]
    public void ThenMessageMarkedAsProcessed()
    {
        Assert.NotNull(_savedMessage); // guard: ! below is safe
        Assert.NotNull(_store.Messages.Single(m => m.Id == _savedMessage!.Id).ProcessedAt);
    }

    [Then("the message is not marked as processed in the store")]
    public void ThenMessageNotMarkedAsProcessed()
    {
        Assert.NotNull(_savedMessage); // guard: ! below is safe
        Assert.Null(_store.Messages.Single(m => m.Id == _savedMessage!.Id).ProcessedAt);
    }

    [Then(@"the message attempt count is (\d+)")]
    public void ThenMessageAttemptCountIs(int expected)
    {
        Assert.NotNull(_savedMessage); // guard: ! below is safe
        Assert.Equal(expected, _store.Messages.Single(m => m.Id == _savedMessage!.Id).AttemptCount);
    }

    [Then("the message is not dead-lettered")]
    public void ThenMessageNotDeadLettered()
    {
        Assert.NotNull(_savedMessage); // guard: ! below is safe
        Assert.Null(_store.Messages.Single(m => m.Id == _savedMessage!.Id).FailedAt);
    }

    [Then("the message is dead-lettered")]
    public void ThenMessageDeadLettered()
    {
        Assert.NotNull(_savedMessage); // guard: ! below is safe
        Assert.NotNull(_store.Messages.Single(m => m.Id == _savedMessage!.Id).FailedAt);
    }

    [Then("an InvalidOperationException is raised")]
    public void ThenInvalidOperationExceptionIsRaised()
    {
        Assert.IsType<InvalidOperationException>(_thrownException);
    }

    // ---- private helpers ----

    private IPublisher BuildPublisher(
        Guid correlationId,
        Guid? causationId,
        IEnumerable<IOutboxTransactionGuard> guards)
    {
        var context = new FakeMessagingContext(correlationId, causationId);
        var options = Microsoft.Extensions.Options.Options.Create(new OpinionatedEventingOptions());
        var registry = new MessageTypeRegistry();
        return new OutboxPublisher(_store, context, registry, options, TimeProvider.System, guards);
    }

    private async Task RunWorkerForOnePassAsync()
    {
        var optionsValue = new OpinionatedEventingOptions();
        optionsValue.Outbox.PollInterval = TimeSpan.Zero;
        optionsValue.Outbox.MaxAttempts = _maxAttempts;

        var services = new ServiceCollection();
        services.AddSingleton<IOutboxStore>(_store);
        services.AddSingleton<ITransport>(_transport);
        var provider = services.BuildServiceProvider();

        var worker = new OutboxDispatcherWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(optionsValue),
            TimeProvider.System,
            NullLogger<OutboxDispatcherWorker>.Instance);

        using var cts = new CancellationTokenSource();
        var task = worker.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var pending = await _store.GetPendingAsync(1);
            if (pending.Count == 0)
                break;

            if (_savedMessage is not null)
            {
                var msg = _store.Messages.SingleOrDefault(m => m.Id == _savedMessage.Id);
                // Dead-lettered messages are excluded from GetPendingAsync, so pending.Count == 0
                // is the reliable exit signal. FailedAt check here is a belt-and-suspenders guard.
                if (msg?.FailedAt is not null)
                    break;
            }

            await Task.Delay(10);
        }

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        await task;
    }

    private static OutboxMessage MakePendingMessage(int attemptCount = 0) => new()
    {
        Id = Guid.NewGuid(),
        MessageType = "Test.Event, Test",
        Payload = "{}",
        MessageKind = MessageKind.Event,
        CorrelationId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
        AttemptCount = attemptCount,
    };

    // ---- inner fakes ----

    private sealed record TestEvent(Guid Id) : IEvent;
    private sealed record TestCommand(Guid Id) : ICommand;

    private sealed class FakeMessagingContext(Guid correlationId, Guid? causationId) : IMessagingContext
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public Guid CorrelationId { get; } = correlationId;
        public Guid? CausationId { get; } = causationId;
    }

    private sealed class FakeTransport : ITransport
    {
        public List<Guid> SentMessageIds { get; } = [];
        public int FailNextCount { get; set; }
        public bool FailAlways { get; set; }

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            if (FailAlways || FailNextCount > 0)
            {
                FailNextCount = Math.Max(0, FailNextCount - 1);
                throw new InvalidOperationException("Transport failure (test).");
            }

            SentMessageIds.Add(message.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingTransactionGuard : IOutboxTransactionGuard
    {
        public void EnsureTransaction() =>
            throw new InvalidOperationException("No active transaction.");
    }
}
