#nullable enable

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using OpinionatedEventing;
using OpinionatedEventing.EntityFramework;
using OpinionatedEventing.Options;
using OpinionatedEventing.Outbox;
using Reqnroll;

namespace OpinionatedEventing.EntityFramework.Specs.StepDefinitions;

[Binding]
public sealed class EntityFrameworkSteps : IAsyncDisposable
{
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private SpecsDbContext? _context;
    private EFCoreOutboxStore<SpecsDbContext>? _store;
    private OutboxMessage? _savedMessage;
    private IReadOnlyList<OutboxMessage>? _pendingMessages;
    private Guid _correlationId;
    private SpecsTestOrder? _order;

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

    // --- IAsyncDisposable ---

    public async ValueTask DisposeAsync()
    {
        if (_context is not null) await _context.DisposeAsync();
    }

    // --- private helpers ---

    private SpecsDbContext GetOrCreateContext()
    {
        if (_context is not null) return _context;
        var options = new DbContextOptionsBuilder<SpecsDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options;
        _context = new SpecsDbContext(options);
        return _context;
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
            modelBuilder.ApplyOutboxConfiguration();
            modelBuilder.Entity<SpecsTestOrder>(b =>
            {
                b.ToTable("specs_test_orders");
                b.HasKey(o => o.Id);
                b.Ignore(o => o.DomainEvents);
            });
        }
    }
}
