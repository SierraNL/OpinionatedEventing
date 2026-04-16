namespace OpinionatedEventing.Sagas;

/// <summary>Represents the lifecycle status of a saga instance.</summary>
public enum SagaStatus
{
    /// <summary>The saga is running and awaiting further events.</summary>
    Active,

    /// <summary>The saga completed successfully.</summary>
    Completed,

    /// <summary>The saga is executing compensation handlers after a failure.</summary>
    Compensating,

    /// <summary>The saga expired before completing.</summary>
    TimedOut,

    /// <summary>The saga failed and could not be compensated.</summary>
    Failed,
}
