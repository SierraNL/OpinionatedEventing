using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Options;
using OpinionatedEventing.Outbox;

namespace OpinionatedEventing.EntityFramework;

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> that harvests domain events from tracked
/// <see cref="IAggregateRoot"/> instances and writes them to the outbox atomically within
/// the same <c>SaveChanges</c> transaction.
/// </summary>
/// <remarks>
/// Register this interceptor with the application's <see cref="DbContext"/> by calling
/// <c>options.AddInterceptors(sp.GetRequiredService&lt;DomainEventInterceptor&gt;())</c>
/// inside the <c>AddDbContext</c> configuration delegate.
/// </remarks>
public sealed class DomainEventInterceptor : SaveChangesInterceptor
{
    private readonly IMessagingContext _messagingContext;
    private readonly IMessageTypeRegistry _registry;
    private readonly IOptions<OpinionatedEventingOptions> _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initialises a new <see cref="DomainEventInterceptor"/>.</summary>
    public DomainEventInterceptor(
        IMessagingContext messagingContext,
        IMessageTypeRegistry registry,
        IOptions<OpinionatedEventingOptions> options,
        TimeProvider timeProvider)
    {
        _messagingContext = messagingContext;
        _registry = registry;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        HarvestDomainEvents(eventData.Context);
        return result;
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        HarvestDomainEvents(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void HarvestDomainEvents(DbContext? context)
    {
        if (context is null) return;

        var aggregates = context.ChangeTracker
            .Entries<IAggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        if (aggregates.Count == 0) return;

        var serializerOptions = _options.Value.SerializerOptions;
        var now = _timeProvider.GetUtcNow();
        var outboxSet = context.Set<OutboxMessage>();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var eventType = domainEvent.GetType();
                outboxSet.Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    MessageType = _registry.GetIdentifier(eventType),
                    Payload = JsonSerializer.Serialize(domainEvent, eventType, serializerOptions),
                    MessageKind = MessageKind.Event,
                    CorrelationId = _messagingContext.CorrelationId,
                    CausationId = _messagingContext.CausationId,
                    CreatedAt = now,
                });
            }

            aggregate.ClearDomainEvents();
        }
    }
}
