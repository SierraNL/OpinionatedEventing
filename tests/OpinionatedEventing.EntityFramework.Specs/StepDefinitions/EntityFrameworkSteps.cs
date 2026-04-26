#nullable enable

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpinionatedEventing;
using OpinionatedEventing.EntityFramework;
using OpinionatedEventing.Options;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Sagas;
using Reqnroll;

namespace OpinionatedEventing.EntityFramework.Specs.StepDefinitions;

[Binding]
public sealed class EntityFrameworkSteps : IAsyncDisposable
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    // Persistent in-memory SQLite connection for outbox-store scenarios.
    // EFCoreOutboxStore.GetPendingAsync uses ExecuteUpdateAsync, which requires a relational provider.
    private readonly SqliteConnection _sqliteConnection = OpenSqliteConnection();
    private SpecsDbContext? _context;
    private EFCoreOutboxStore<SpecsDbContext>? _store;
    private OutboxMessage? _savedMessage;
    private IReadOnlyList<OutboxMessage>? _pendingMessages;
    private Guid _correlationId;
    private SpecsTestOrder? _order;

    // --- saga state store fields ---
    private ServiceProvider? _sagaServiceProvider;
    private IServiceScope? _sagaScope;
    private ISagaStateStore? _sagaStore;
    private SagaState? _sagaState;
    private SagaState? _foundSagaState;
    private IReadOnlyList<SagaState>? _expiredSagaStates;

    // --- Given ---

    [Given("an order aggregate with a pending domain event")]
    public void GivenOrderAggregateWithPendingDomainEvent()
    {
        _order = SpecsTestOrder.Place(Guid.NewGuid());
    }

    [Given("a known messaging context correlation ID")]
    public void GivenKnownCorrelationId()
    {
        _correlationId = Guid.NewGuid();
    }

    [Given("a new saga state for type {string} with correlation ID {string}")]
    public void GivenNewSagaState(string sagaType, string correlationId)
    {
        _sagaState = new SagaState
        {
            Id = Guid.NewGuid(),
            SagaType = sagaType,
            CorrelationId = correlationId,
            State = "{}",
            Status = SagaStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _sagaStore ??= GetOrCreateSagaStore();
    }

    [Given("the saga state expires in the past")]
    public void GivenSagaStateExpiresInThePast()
    {
        _sagaState!.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
    }

    [Given("the saga state status is Completed")]
    public void GivenSagaStateStatusIsCompleted()
    {
        _sagaState!.Status = SagaStatus.Completed;
    }

    [Given("the saga state is saved via EFCoreSagaStateStore")]
    public async Task GivenSagaStateSaved()
    {
        await _sagaStore!.SaveAsync(_sagaState!);
    }

    [Given("a message is staged and committed via EFCoreOutboxStore")]
    public async Task GivenMessageStagedAndCommitted()
    {
        var context = GetOrCreateContext();
        _store = new EFCoreOutboxStore<SpecsDbContext>(context, TimeProvider.System);
        _savedMessage = MakeMessage();
        await _store.SaveAsync(_savedMessage);
        await context.SaveChangesAsync();
    }

    // --- When ---

    [When("SaveChangesAsync is called with the DomainEventInterceptor active")]
    public async Task WhenSaveChangesWithInterceptorActive()
    {
        var corrId = _correlationId == Guid.Empty ? Guid.NewGuid() : _correlationId;
        var messagingContext = new FakeMessagingContext(corrId);
        var opts = Microsoft.Extensions.Options.Options.Create(new OpinionatedEventingOptions());
        var interceptor = new DomainEventInterceptor(messagingContext, opts, TimeProvider.System);

        var options = new DbContextOptionsBuilder<SpecsDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .AddInterceptors(interceptor)
            .Options;
        _context = new SpecsDbContext(options);

        // _order is always set by GivenOrderAggregateWithPendingDomainEvent before this step runs.
        _context.Set<SpecsTestOrder>().Add(_order!);
        await _context.SaveChangesAsync();
    }

    [When("SaveChanges is called synchronously with the DomainEventInterceptor active")]
    public void WhenSaveChangesSyncWithInterceptorActive()
    {
        var messagingContext = new FakeMessagingContext(Guid.NewGuid());
        var opts = Microsoft.Extensions.Options.Options.Create(new OpinionatedEventingOptions());
        var interceptor = new DomainEventInterceptor(messagingContext, opts, TimeProvider.System);

        var options = new DbContextOptionsBuilder<SpecsDbContext>()
            .UseInMemoryDatabase(_databaseName + "-sync")
            .AddInterceptors(interceptor)
            .Options;
        _context = new SpecsDbContext(options);

        // _order is always set by GivenOrderAggregateWithPendingDomainEvent before this step runs.
        _context.Set<SpecsTestOrder>().Add(_order!);
        _context.SaveChanges();
    }

    [When("pending messages are queried")]
    public async Task WhenPendingMessagesQueried()
    {
        _pendingMessages = await _store!.GetPendingAsync(10);
    }

    [When("the message is marked as processed")]
    public async Task WhenMessageMarkedAsProcessed()
    {
        await _store!.MarkProcessedAsync(_savedMessage!.Id);
    }

    [When("the message is marked as failed")]
    public async Task WhenMessageMarkedAsFailed()
    {
        await _store!.MarkFailedAsync(_savedMessage!.Id, "test error");
    }

    [When("the message attempt count is incremented with error {string}")]
    public async Task WhenMessageAttemptCountIncremented(string error)
    {
        await _store!.IncrementAttemptAsync(_savedMessage!.Id, error);
    }

    [When("MarkProcessedAsync is called with an unknown message ID")]
    public async Task WhenMarkProcessedCalledWithUnknownId()
    {
        var context = GetOrCreateContext();
        _store = new EFCoreOutboxStore<SpecsDbContext>(context, TimeProvider.System);
        await _store.MarkProcessedAsync(Guid.NewGuid());
    }

    [When("MarkFailedAsync is called with an unknown message ID")]
    public async Task WhenMarkFailedCalledWithUnknownId()
    {
        var context = GetOrCreateContext();
        _store = new EFCoreOutboxStore<SpecsDbContext>(context, TimeProvider.System);
        await _store.MarkFailedAsync(Guid.NewGuid(), "unknown");
    }

    [When("IncrementAttemptAsync is called with an unknown message ID")]
    public async Task WhenIncrementAttemptCalledWithUnknownId()
    {
        var context = GetOrCreateContext();
        _store = new EFCoreOutboxStore<SpecsDbContext>(context, TimeProvider.System);
        await _store.IncrementAttemptAsync(Guid.NewGuid(), "unknown");
    }

    [When("the saga state is saved via EFCoreSagaStateStore")]
    public async Task WhenSagaStateSaved()
    {
        await _sagaStore!.SaveAsync(_sagaState!);
    }

    [When("the saga state status is updated to Completed")]
    public async Task WhenSagaStateStatusUpdatedToCompleted()
    {
        _sagaState!.Status = SagaStatus.Completed;
        await _sagaStore!.UpdateAsync(_sagaState);
    }

    [When("a saga state is looked up with type {string} and correlation ID {string}")]
    public async Task WhenSagaStateLookedUp(string sagaType, string correlationId)
    {
        _sagaStore ??= GetOrCreateSagaStore();
        _foundSagaState = await _sagaStore.FindAsync(sagaType, correlationId);
    }

    // --- Then ---

    [Then("one outbox message with kind {string} is written to the database")]
    public void ThenOneOutboxMessageWithKindWritten(string kind)
    {
        var messages = _context!.Set<OutboxMessage>().ToList();
        Xunit.Assert.Single(messages);
        Xunit.Assert.Equal(kind, messages[0].MessageKind);
    }

    [Then("the outbox message carries the messaging context correlation ID")]
    public void ThenOutboxMessageCarriesCorrelationId()
    {
        var message = _context!.Set<OutboxMessage>().Single();
        Xunit.Assert.Equal(_correlationId, message.CorrelationId);
    }

    [Then("the aggregate has no remaining domain events")]
    public void ThenAggregateHasNoDomainEvents()
    {
        Xunit.Assert.Empty(_order!.DomainEvents);
    }

    [Then("the staged message is in the result")]
    public void ThenStagedMessageIsInResult()
    {
        Xunit.Assert.NotNull(_pendingMessages);
        Xunit.Assert.Contains(_pendingMessages!, m => m.Id == _savedMessage!.Id);
    }

    [Then("no pending messages are returned")]
    public void ThenNoPendingMessagesReturned()
    {
        Xunit.Assert.NotNull(_pendingMessages);
        Xunit.Assert.Empty(_pendingMessages!);
    }

    [Then("the message attempt count is {int} and error is {string}")]
    public async Task ThenMessageAttemptCountIsWithError(int expectedCount, string expectedError)
    {
        var messages = await _store!.GetPendingAsync(10);
        var message = Xunit.Assert.Single(messages);
        Xunit.Assert.Equal(expectedCount, message.AttemptCount);
        Xunit.Assert.Equal(expectedError, message.Error);
    }

    [Then("the saga state can be found by type {string} and correlation ID {string}")]
    public async Task ThenSagaStateCanBeFound(string sagaType, string correlationId)
    {
        _foundSagaState = await _sagaStore!.FindAsync(sagaType, correlationId);
        Xunit.Assert.NotNull(_foundSagaState);
        Xunit.Assert.Equal(sagaType, _foundSagaState.SagaType);
        Xunit.Assert.Equal(correlationId, _foundSagaState.CorrelationId);
    }

    [Then("the found saga state has status Completed")]
    public async Task ThenFoundSagaStateHasStatusCompleted()
    {
        var found = await _sagaStore!.FindAsync(_sagaState!.SagaType, _sagaState.CorrelationId);
        Xunit.Assert.Equal(SagaStatus.Completed, found!.Status);
    }

    [Then("the found saga state is null")]
    public void ThenFoundSagaStateIsNull()
    {
        Xunit.Assert.Null(_foundSagaState);
    }

    [Then("the expired saga query returns the saga state")]
    public async Task ThenExpiredSagaQueryReturnsState()
    {
        _expiredSagaStates = await _sagaStore!.GetExpiredAsync(DateTimeOffset.UtcNow);
        Xunit.Assert.Contains(_expiredSagaStates, s => s.CorrelationId == _sagaState!.CorrelationId);
    }

    [Then("the expired saga query returns no results")]
    public async Task ThenExpiredSagaQueryReturnsNoResults()
    {
        _expiredSagaStates = await _sagaStore!.GetExpiredAsync(DateTimeOffset.UtcNow);
        Xunit.Assert.Empty(_expiredSagaStates);
    }

    // --- IAsyncDisposable ---

    public async ValueTask DisposeAsync()
    {
        if (_context is not null) await _context.DisposeAsync();
        _sagaScope?.Dispose();
        if (_sagaServiceProvider is not null) await _sagaServiceProvider.DisposeAsync();
        _sqliteConnection.Dispose();
    }

    // --- private helpers ---

    private ISagaStateStore GetOrCreateSagaStore()
    {
        var sagaDbName = _databaseName + "-saga";
        var services = new ServiceCollection();
        services.AddDbContext<SpecsSagaDbContext>(opts => opts.UseInMemoryDatabase(sagaDbName));
        services.AddOpinionatedEventingEntityFramework<SpecsSagaDbContext>();
        _sagaServiceProvider = services.BuildServiceProvider(validateScopes: true);
        _sagaScope = _sagaServiceProvider.CreateScope();
        return _sagaScope.ServiceProvider.GetRequiredService<ISagaStateStore>();
    }

    private SpecsDbContext GetOrCreateContext()
    {
        if (_context is not null) return _context;
        _context = new SpecsDbContext(new DbContextOptionsBuilder<SpecsDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options);
        _context.Database.EnsureCreated();
        return _context;
    }

    private static SqliteConnection OpenSqliteConnection()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        return conn;
    }

    private static OutboxMessage MakeMessage() => new()
    {
        Id = Guid.NewGuid(),
        MessageType = "SomeType, SomeAssembly",
        Payload = "{}",
        MessageKind = "Event",
        CorrelationId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // --- inner types ---

    private sealed class FakeMessagingContext(Guid correlationId) : IMessagingContext
    {
        public Guid CorrelationId { get; } = correlationId;
        public Guid? CausationId => null;
    }

    private sealed class SpecsOrderPlaced : IEvent
    {
        public Guid OrderId { get; init; }
    }

    private sealed class SpecsTestOrder : AggregateRoot
    {
        public Guid Id { get; init; }

        public static SpecsTestOrder Place(Guid id)
        {
            var order = new SpecsTestOrder { Id = id };
            order.RaiseDomainEvent(new SpecsOrderPlaced { OrderId = id });
            return order;
        }
    }

    private sealed class SpecsDbContext : DbContext
    {
        public SpecsDbContext(DbContextOptions<SpecsDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyOutboxConfiguration(Database.ProviderName);
            modelBuilder.Entity<SpecsTestOrder>(b =>
            {
                b.ToTable("specs_test_orders");
                b.HasKey(o => o.Id);
                b.Ignore(o => o.DomainEvents);
            });
        }
    }

    private sealed class SpecsSagaDbContext : DbContext
    {
        public SpecsSagaDbContext(DbContextOptions<SpecsSagaDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyOutboxConfiguration();
            modelBuilder.ApplySagaStateConfiguration();
        }
    }
}
