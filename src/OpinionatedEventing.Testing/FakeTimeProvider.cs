#nullable enable

namespace OpinionatedEventing.Testing;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock advances only when <see cref="Advance"/> is called.
/// Supports <see cref="CreateTimer"/> so that <c>Task.Delay(interval, fakeTimeProvider, ct)</c>
/// fires immediately when the fake clock is advanced past the delay.
/// Use in tests that need to control the passage of time without sleeping.
/// Not for production use.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    private readonly List<FakeTimer> _timers = [];
    private readonly object _lock = new();

    /// <summary>Initialises the fake clock at <paramref name="startTime"/>.</summary>
    public FakeTimeProvider(DateTimeOffset startTime) => _utcNow = startTime;

    /// <summary>Initialises the fake clock at <see cref="DateTimeOffset.UtcNow"/>.</summary>
    public FakeTimeProvider() => _utcNow = DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock) return _utcNow;
    }

    /// <summary>Overrides the current fake time to <paramref name="time"/>.</summary>
    public void SetUtcNow(DateTimeOffset time)
    {
        lock (_lock) _utcNow = time;
    }

    /// <summary>
    /// Advances the fake clock by <paramref name="delta"/> and fires any timers whose
    /// due time has elapsed as a result.
    /// </summary>
    public void Advance(TimeSpan delta)
    {
        List<FakeTimer> toFire;
        lock (_lock)
        {
            _utcNow = _utcNow.Add(delta);
            toFire = _timers.Where(t => !t.IsDisposed && t.NextFireTime <= _utcNow).ToList();
        }
        foreach (var t in toFire)
            t.Fire();
    }

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        FakeTimer timer;
        lock (_lock)
        {
            var nextFireTime = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : _utcNow.Add(dueTime);
            timer = new FakeTimer(callback, state, nextFireTime, period, this);
            _timers.Add(timer);
        }
        return timer;
    }

    internal void RemoveTimer(FakeTimer timer)
    {
        lock (_lock)
            _timers.Remove(timer);
    }

    internal sealed class FakeTimer : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly object? _callbackState;
        private readonly TimeSpan _period;
        private readonly FakeTimeProvider _provider;
        private DateTimeOffset _nextFireTime;

        public bool IsDisposed { get; private set; }

        public DateTimeOffset NextFireTime
        {
            get { lock (_provider._lock) return _nextFireTime; }
        }

        public FakeTimer(
            TimerCallback callback,
            object? callbackState,
            DateTimeOffset nextFireTime,
            TimeSpan period,
            FakeTimeProvider provider)
        {
            _callback = callback;
            _callbackState = callbackState;
            _nextFireTime = nextFireTime;
            _period = period;
            _provider = provider;
        }

        public void Fire()
        {
            if (IsDisposed) return;
            _callback(_callbackState);
            lock (_provider._lock)
            {
                if (_period == Timeout.InfiniteTimeSpan || _period <= TimeSpan.Zero)
                    IsDisposed = true;
                else
                    _nextFireTime = _nextFireTime.Add(_period);
            }
            if (IsDisposed)
                _provider.RemoveTimer(this);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (IsDisposed) return false;
            lock (_provider._lock)
            {
                _nextFireTime = dueTime == Timeout.InfiniteTimeSpan
                    ? DateTimeOffset.MaxValue
                    : _provider._utcNow.Add(dueTime);
            }
            return true;
        }

        public void Dispose()
        {
            IsDisposed = true;
            _provider.RemoveTimer(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
