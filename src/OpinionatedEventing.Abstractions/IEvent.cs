namespace OpinionatedEventing;

/// <summary>
/// Marker interface for domain/integration events.
/// Events represent something that has already happened.
/// Implementations must be immutable — use <c>record</c> types.
/// </summary>
public interface IEvent { }
