namespace OpinionatedEventing.Outbox;

/// <summary>Discriminates between the two message flavours supported by the outbox.</summary>
public enum MessageKind
{
    /// <summary>A domain event published to one or more subscribers.</summary>
    Event,

    /// <summary>A command addressed to exactly one handler.</summary>
    Command,
}
