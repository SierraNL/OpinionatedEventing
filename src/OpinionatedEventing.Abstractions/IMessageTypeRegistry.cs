using OpinionatedEventing.Attributes;

namespace OpinionatedEventing;

/// <summary>
/// Maps between stable on-the-wire message-type identifiers and CLR types.
/// The default identifier for a type is its <c>FullName</c>; a
/// <see cref="MessageTypeAttribute"/> on the contract record overrides it.
/// </summary>
/// <remarks>
/// Registered as a singleton. Populated by
/// <c>AddHandlersFromAssemblies</c> for all handled event and command types.
/// Additional types can be registered explicitly via <see cref="Register"/>.
/// </remarks>
public interface IMessageTypeRegistry
{
    /// <summary>
    /// Registers <paramref name="type"/> in the registry using its
    /// <see cref="MessageTypeAttribute"/> identifier if present, or <c>FullName</c> otherwise.
    /// Registering the same type more than once is idempotent.
    /// </summary>
    /// <param name="type">The CLR type to register.</param>
    void Register(Type type);

    /// <summary>
    /// Returns the stable identifier for <paramref name="type"/>.
    /// If the type is registered the stored identifier is returned; otherwise
    /// the <see cref="MessageTypeAttribute"/> identifier is used if present,
    /// falling back to <c>type.FullName</c>.
    /// </summary>
    /// <param name="type">The CLR type whose identifier is requested.</param>
    /// <returns>The stable on-the-wire identifier string.</returns>
    string GetIdentifier(Type type);

    /// <summary>
    /// Resolves a CLR type from its on-the-wire <paramref name="identifier"/>.
    /// Resolution order:
    /// <list type="number">
    ///   <item>Exact match in the registry dictionary.</item>
    ///   <item>Linear scan of loaded assemblies by <c>FullName</c> (catches types not yet registered).</item>
    ///   <item><see cref="Type.GetType(string)"/> — backwards-compatibility fallback for existing
    ///         outbox rows or broker messages that carry the old <c>AssemblyQualifiedName</c> format.</item>
    /// </list>
    /// </summary>
    /// <param name="identifier">The on-the-wire type identifier.</param>
    /// <returns>The resolved CLR type.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no matching type can be found via any resolution strategy.
    /// </exception>
    Type Resolve(string identifier);
}
