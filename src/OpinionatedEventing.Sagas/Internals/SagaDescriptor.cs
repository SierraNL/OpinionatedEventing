#nullable enable

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpinionatedEventing.Sagas.Diagnostics;

namespace OpinionatedEventing.Sagas;

internal abstract class SagaDescriptor
{
    public abstract string SagaTypeName { get; }

    public abstract Task HandleEventAsync(
        object @event,
        IServiceProvider sp,
        ISagaStateStore store,
        IPublisher publisher,
        TimeProvider timeProvider,
        JsonSerializerOptions? serializerOptions,
        CancellationToken ct);

    public abstract Task HandleTimeoutAsync(
        SagaState state,
        IServiceProvider sp,
        ISagaStateStore store,
        IPublisher publisher,
        TimeProvider timeProvider,
        JsonSerializerOptions? serializerOptions,
        CancellationToken ct);
}

internal sealed class SagaDescriptor<TOrchestrator, TSagaState> : SagaDescriptor
    where TOrchestrator : SagaOrchestrator<TSagaState>
    where TSagaState : class, new()
{
    public override string SagaTypeName { get; } = typeof(TOrchestrator).AssemblyQualifiedName!;

    public override async Task HandleEventAsync(
        object @event,
        IServiceProvider sp,
        ISagaStateStore store,
        IPublisher publisher,
        TimeProvider timeProvider,
        JsonSerializerOptions? serializerOptions,
        CancellationToken ct)
    {
        var orchestrator = sp.GetRequiredService<TOrchestrator>();
        var def = orchestrator.GetDefinition();
        var eventType = @event.GetType();

        bool isNormal = def.EventHandlers.TryGetValue(eventType, out var handler);
        bool isCompensation = def.CompensationHandlerByType.TryGetValue(eventType, out var compensationHandler);

        if (!isNormal && !isCompensation) return;

        var correlationKey = GetCorrelationKey(@event, def, eventType);
        if (correlationKey is null) return;

        var state = await store.FindAsync(SagaTypeName, correlationKey, ct);
        bool isNew = state is null;

        if (isNew)
        {
            if (eventType != def.StartEventType) return;

            state = new SagaState
            {
                Id = Guid.NewGuid(),
                SagaType = SagaTypeName,
                CorrelationId = correlationKey,
                State = JsonSerializer.Serialize(new TSagaState(), serializerOptions),
                Status = SagaStatus.Active,
                CreatedAt = timeProvider.GetUtcNow(),
                UpdatedAt = timeProvider.GetUtcNow(),
                ExpiresAt = CalculateExpiry(def, timeProvider),
            };
        }
        else if (state!.Status is SagaStatus.Completed or SagaStatus.Failed)
        {
            return;
        }

        _ = Guid.TryParse(correlationKey, out var corrGuid);
        var sagaStateObj = JsonSerializer.Deserialize<TSagaState>(state.State, serializerOptions)!;
        var context = new SagaContext(corrGuid, publisher, ct);

        using var activity = SagaDiagnostics.StartSagaStepActivity(typeof(TOrchestrator).Name, correlationKey, eventType.FullName ?? eventType.Name);
        bool countedActive = false;

        try
        {
            if (isCompensation && state.Status == SagaStatus.Active)
            {
                state.Status = SagaStatus.Compensating;
                await compensationHandler!(@event, sagaStateObj, context);
            }
            else if (isNormal && state.Status == SagaStatus.Active)
            {
                await handler!(@event, sagaStateObj, context);
            }

            if (context.IsCompleted)
                state.Status = SagaStatus.Completed;

            state.State = JsonSerializer.Serialize(sagaStateObj, serializerOptions);
            state.UpdatedAt = timeProvider.GetUtcNow();

            if (isNew)
            {
                await store.SaveAsync(state, ct);
                // Only count as active after the save succeeds; skip if already completed.
                if (state.Status != SagaStatus.Completed)
                {
                    SagaDiagnostics.Active.Add(1);
                    countedActive = true;
                }
            }
            else
            {
                await store.UpdateAsync(state, ct);
                if (context.IsCompleted)
                    SagaDiagnostics.Active.Add(-1);
            }
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            if (state.Status == SagaStatus.Compensating)
            {
                state.Status = SagaStatus.Failed;
                if (countedActive)
                    SagaDiagnostics.Active.Add(-1);
                else if (!isNew)
                    SagaDiagnostics.Active.Add(-1);
            }

            state.State = JsonSerializer.Serialize(sagaStateObj, serializerOptions);
            state.UpdatedAt = timeProvider.GetUtcNow();

            if (!isNew)
                await store.UpdateAsync(state, ct);

            throw;
        }
    }

    public override async Task HandleTimeoutAsync(
        SagaState state,
        IServiceProvider sp,
        ISagaStateStore store,
        IPublisher publisher,
        TimeProvider timeProvider,
        JsonSerializerOptions? serializerOptions,
        CancellationToken ct)
    {
        var orchestrator = sp.GetRequiredService<TOrchestrator>();
        var def = orchestrator.GetDefinition();

        if (def.TimeoutHandler is null) return;

        // Mark timed-out once, before attempting the handler, so the counter fires exactly
        // once per timeout event even if the handler or store call later throws.
        state.Status = SagaStatus.TimedOut;
        SagaDiagnostics.TimedOut.Add(1);

        var sagaStateObj = JsonSerializer.Deserialize<TSagaState>(state.State, serializerOptions)!;
        _ = Guid.TryParse(state.CorrelationId, out var corrGuid);
        var context = new SagaContext(corrGuid, publisher, ct);

        try
        {
            await def.TimeoutHandler(sagaStateObj, context);

            if (context.IsCompleted)
                state.Status = SagaStatus.Completed;

            state.State = JsonSerializer.Serialize(sagaStateObj, serializerOptions);
            state.UpdatedAt = timeProvider.GetUtcNow();
            await store.UpdateAsync(state, ct);
            SagaDiagnostics.Active.Add(-1);
        }
        catch
        {
            state.Status = SagaStatus.Failed;
            state.State = JsonSerializer.Serialize(sagaStateObj, serializerOptions);
            state.UpdatedAt = timeProvider.GetUtcNow();
            await store.UpdateAsync(state, ct);
            SagaDiagnostics.Active.Add(-1);
            throw;
        }
    }

    private static string? GetCorrelationKey(
        object @event,
        SagaDefinition<TSagaState> def,
        Type eventType)
    {
        if (def.CorrelationExpressions.TryGetValue(eventType, out var expr))
            return expr(@event);

        var prop = eventType.GetProperty("CorrelationId");
        return prop?.GetValue(@event)?.ToString();
    }

    private static DateTimeOffset? CalculateExpiry(SagaDefinition<TSagaState> def, TimeProvider timeProvider)
    {
        if (def.ExpiresAt.HasValue) return def.ExpiresAt;
        if (def.ExpiresAfter.HasValue) return timeProvider.GetUtcNow().Add(def.ExpiresAfter.Value);
        return null;
    }
}
