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
    public async Task Worker_cycle_fires_when_FakeTimeProvider_advances_past_check_interval()
    {
        // Verifies that Task.Delay uses the TimeProvider-aware overload: advancing FakeTimeProvider
        // past the check interval must trigger the next worker cycle without any real-time wait.
        //
        // Design: start the worker with an empty store so the first check finds nothing and the
        // worker immediately blocks on Task.Delay. A brief real-time pause ensures the timer is
        // registered before we call Advance — eliminating the race where Advance fires before
        // the timer exists.
        var clock = new FakeTimeProvider();
        await using var h = SagaTestHarness.Create(s => s.AddSaga<TimedOrderSaga>(), clock);
        var ct = TestContext.Current.CancellationToken;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = h.Worker.StartAsync(cts.Token);

        // Wait (real time) for the first empty check to complete and the worker to register
        // its Task.Delay timer with the fake clock. The check finds no sagas so it returns
        // almost immediately; 200 ms is a generous real-time budget for any CI runner.
        await Task.Delay(200, ct);

        // Dispatch a saga that expires right now (relative to the fake clock).
        var corrId = Guid.NewGuid();
        await h.Dispatcher.DispatchAsync(new OrderPlaced { CorrelationId = corrId }, ct);
        clock.Advance(TimeSpan.FromMinutes(31)); // expire the saga

        // Advance the fake clock past the 30-second check interval. This fires the Task.Delay
        // timer synchronously, queuing the second CheckTimeoutsAsync on the thread pool.
        // Without the TimeProvider-aware overload this advance would have no effect.
        clock.Advance(TimeSpan.FromSeconds(31));

        // Poll (real time) until the second check completes.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var s = await h.Store.FindAsync(typeof(TimedOrderSaga).FullName!, corrId.ToString(), ct);
            if (s?.Status == SagaStatus.Completed) break;
            await Task.Delay(10, ct);
        }

        var state = await h.Store.FindAsync(typeof(TimedOrderSaga).FullName!, corrId.ToString(), ct);
        Assert.Equal(SagaStatus.Completed, state!.Status);

        await cts.CancelAsync();
        await h.Worker.StopAsync(CancellationToken.None);
    }
}
