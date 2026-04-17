using Microsoft.Extensions.DependencyInjection;
using OpinionatedEventing.Sagas;
using OpinionatedEventing.Sagas.Tests.TestSupport;
using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Sagas.Tests;

public sealed class SagaTimeoutWorkerTests
{
    [Fact]
    public async Task Expired_saga_transitions_to_completed_with_accelerated_clock()
    {
        var clock = new FakeTimeProvider();
        await using var h = SagaTestHarness.Create(s => s.AddSaga<TimedOrderSaga>(), clock);
        var ct = TestContext.Current.CancellationToken;
        var corrId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId }, ct);

        // Verify expiry was calculated relative to the fake clock.
        var state = await h.Store.FindAsync(typeof(TimedOrderSaga).AssemblyQualifiedName!, corrId.ToString(), ct);
        Assert.NotNull(state!.ExpiresAt);

        // Advance past the 30-minute expiry — GetExpiredAsync should now return it.
        clock.Advance(TimeSpan.FromMinutes(31));

        var expired = await h.Store.GetExpiredAsync(clock.GetUtcNow(), ct);
        Assert.Single(expired);

        var descriptor = h.Scope.ServiceProvider.GetServices<SagaDescriptor>().Single();
        await descriptor.HandleTimeoutAsync(state, h.Scope.ServiceProvider, h.Store, h.Publisher, clock, null, ct);

        Assert.Equal(SagaStatus.Completed, state.Status);
        var sagaState = System.Text.Json.JsonSerializer.Deserialize<TimedOrderSagaState>(state.State)!;
        Assert.True(sagaState.TimedOut);
    }

    [Fact]
    public async Task Saga_transitions_to_failed_when_timeout_handler_throws()
    {
        var clock = new FakeTimeProvider();
        await using var h = SagaTestHarness.Create(s => s.AddSaga<ThrowingTimeoutSaga>(), clock);
        var ct = TestContext.Current.CancellationToken;
        var corrId = Guid.NewGuid();

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId }, ct);

        clock.Advance(TimeSpan.FromMinutes(31));

        var expired = await h.Store.GetExpiredAsync(clock.GetUtcNow(), ct);
        var state = Assert.Single(expired);

        var descriptor = h.Scope.ServiceProvider.GetServices<SagaDescriptor>().Single();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            descriptor.HandleTimeoutAsync(state, h.Scope.ServiceProvider, h.Store, h.Publisher, clock, null, ct));

        Assert.Equal(SagaStatus.Failed, state.Status);
    }

    [Fact]
    public async Task Saga_before_expiry_is_not_returned_by_GetExpiredAsync()
    {
        var clock = new FakeTimeProvider();
        await using var h = SagaTestHarness.Create(s => s.AddSaga<TimedOrderSaga>(), clock);
        var ct = TestContext.Current.CancellationToken;

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = Guid.NewGuid() }, ct);

        // Only 10 minutes have passed — not yet expired.
        clock.Advance(TimeSpan.FromMinutes(10));

        var expired = await h.Store.GetExpiredAsync(clock.GetUtcNow(), ct);
        Assert.Empty(expired);
    }
}
