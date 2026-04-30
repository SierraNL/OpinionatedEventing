namespace OpinionatedEventing.Attributes;

/// <summary>
/// Overrides the stable on-the-wire type identifier used by <see cref="IMessageTypeRegistry"/>
/// for a message contract. Apply to event or command records when the default identifier
/// (the CLR <c>FullName</c>) is not stable enough — for example after renaming a namespace.
/// </summary>
/// <remarks>
/// The identifier must be unique across all registered message types in a service.
/// To preserve compatibility with messages already in the outbox or broker after a rename,
/// set this attribute on the new type with the old type's original identifier.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class MessageTypeAttribute : Attribute
{
    /// <summary>Gets the stable identifier for this message type.</summary>
    public string Identifier { get; }

    /// <summary>Initialises a new <see cref="MessageTypeAttribute"/>.</summary>
    /// <param name="identifier">The stable on-the-wire identifier for this message type.</param>
    public MessageTypeAttribute(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        Identifier = identifier;
    }
}
