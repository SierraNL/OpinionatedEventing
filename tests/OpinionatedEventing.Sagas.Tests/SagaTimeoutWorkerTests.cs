using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        var state = await h.Store.FindAsync(typeof(TimedOrderSaga).FullName!, corrId.ToString(), ct);
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

    [Fact]
    public async Task GetExpiredAsync_does_not_return_same_saga_twice_to_concurrent_callers()
    {
        // Verifies the in-memory claim-column invariant: once the store has claimed a saga for
        // one caller, a second concurrent caller must not receive the same saga.
        var clock = new FakeTimeProvider();
        await using var h = SagaTestHarness.Create(s => s.AddSaga<TimedOrderSaga>(), clock);
        var ct = TestContext.Current.CancellationToken;

        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = Guid.NewGuid() }, ct);
        clock.Advance(TimeSpan.FromMinutes(31));

        var batch1 = await h.Store.GetExpiredAsync(clock.GetUtcNow(), ct);
        var batch2 = await h.Store.GetExpiredAsync(clock.GetUtcNow(), ct);

        Assert.Single(batch1);
        Assert.Empty(batch2);
    }

    [Fact]
    public async Task Worker_second_cycle_fires_when_FakeTimeProvider_advances_past_check_interval()
    {
        // Verifies that Task.Delay uses the TimeProvider-aware overload: advancing FakeTimeProvider
        // past the check interval must trigger the next worker cycle without any real-time wait.
        var clock = new FakeTimeProvider();
        await using var h = SagaTestHarness.Create(s => s.AddSaga<TimedOrderSaga>(), clock);
        var ct = TestContext.Current.CancellationToken;

        // First saga: already expired so the first worker check catches it immediately.
        var corrId1 = Guid.NewGuid();
        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId1 }, ct);
        clock.Advance(TimeSpan.FromMinutes(31));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = h.Worker.StartAsync(cts.Token);

        // Poll (real time) until the first check completes and the worker is waiting on Task.Delay.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var s = await h.Store.FindAsync(typeof(TimedOrderSaga).FullName!, corrId1.ToString(), ct);
            if (s?.Status == SagaStatus.Completed) break;
            await Task.Delay(10, ct);
        }
        Assert.Equal(SagaStatus.Completed,
            (await h.Store.FindAsync(typeof(TimedOrderSaga).FullName!, corrId1.ToString(), ct))!.Status);

        // Second saga: dispatched now (fake time = T+31 min), expires in 30 min.
        var corrId2 = Guid.NewGuid();
        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId2 }, ct);

        // Advance fake clock 31 minutes: both expires the second saga AND ticks past the
        // 30-second check interval, unblocking Task.Delay and triggering the second check.
        // Without the TimeProvider-aware Task.Delay overload this advance would have no effect.
        clock.Advance(TimeSpan.FromMinutes(31));

        deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var s = await h.Store.FindAsync(typeof(TimedOrderSaga).FullName!, corrId2.ToString(), ct);
            if (s?.Status == SagaStatus.Completed) break;
            await Task.Delay(10, ct);
        }

        var state2 = await h.Store.FindAsync(typeof(TimedOrderSaga).FullName!, corrId2.ToString(), ct);
        Assert.Equal(SagaStatus.Completed, state2!.Status);

        await cts.CancelAsync();
        await h.Worker.StopAsync(CancellationToken.None);
    }
}
