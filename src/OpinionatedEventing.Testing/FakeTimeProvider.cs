#nullable enable

namespace OpinionatedEventing.Testing;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock advances only when <see cref="Advance"/> is called.
/// Use in tests that need to control the passage of time without sleeping.
/// Not for production use.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    /// <summary>Initialises the fake clock at <paramref name="startTime"/>.</summary>
    public FakeTimeProvider(DateTimeOffset startTime) => _utcNow = startTime;

    /// <summary>Initialises the fake clock at <see cref="DateTimeOffset.UtcNow"/>.</summary>
    public FakeTimeProvider() => _utcNow = DateTimeOffset.UtcNow;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _utcNow;

    /// <summary>Advances the fake clock by <paramref name="delta"/>.</summary>
    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}
