#nullable enable

using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Testing.Tests;

public sealed class FakeTimeProviderTests
{
    [Fact]
    public void GetUtcNow_returns_start_time()
    {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(start);

        Assert.Equal(start, clock.GetUtcNow());
    }

    [Fact]
    public void Advance_moves_clock_forward()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var before = clock.GetUtcNow();

        clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(before.AddMinutes(10), clock.GetUtcNow());
    }

    [Fact]
    public void SetUtcNow_overrides_current_time()
    {
        var clock = new FakeTimeProvider();
        var target = new DateTimeOffset(2030, 6, 15, 12, 0, 0, TimeSpan.Zero);

        clock.SetUtcNow(target);

        Assert.Equal(target, clock.GetUtcNow());
    }

    [Fact]
    public void Advance_fires_one_shot_timer_when_due_time_elapses()
    {
        var clock = new FakeTimeProvider();
        int fired = 0;

        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(0, fired);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Advance_does_not_fire_one_shot_timer_twice()
    {
        var clock = new FakeTimeProvider();
        int fired = 0;

        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Repeating_timer_fires_on_each_advance_past_its_period()
    {
        // Advance collects the fire list once per call, so a repeating timer fires once per Advance.
        var clock = new FakeTimeProvider();
        int fired = 0;

        using var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, fired);

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(2, fired);

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(3, fired);
    }

    [Fact]
    public void Disposed_timer_does_not_fire_on_advance()
    {
        var clock = new FakeTimeProvider();
        int fired = 0;

        var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        timer.Dispose();

        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task DisposeAsync_prevents_timer_from_firing()
    {
        var clock = new FakeTimeProvider();
        int fired = 0;

        var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        await timer.DisposeAsync();

        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Change_reschedules_timer_to_new_due_time()
    {
        var clock = new FakeTimeProvider();
        int fired = 0;

        var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);

        // Reschedule to fire in 5 seconds from now.
        timer.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(1, fired);
        timer.Dispose();
    }

    [Fact]
    public void Change_on_disposed_timer_returns_false()
    {
        var clock = new FakeTimeProvider();
        var timer = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        timer.Dispose();

        var result = timer.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        Assert.False(result);
    }
}
