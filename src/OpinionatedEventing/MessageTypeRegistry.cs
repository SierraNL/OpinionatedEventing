#nullable enable

using System.Collections.Concurrent;
using System.Reflection;
using OpinionatedEventing.Attributes;

namespace OpinionatedEventing;

/// <summary>
/// Thread-safe singleton implementation of <see cref="IMessageTypeRegistry"/>.
/// </summary>
public sealed class MessageTypeRegistry : IMessageTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _identifierToType = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string> _typeToIdentifier = new();

    /// <inheritdoc/>
    public void Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        string identifier = GetAttributeIdentifier(type) ?? type.FullName!;

        // Guard against two different types claiming the same on-the-wire identifier.
        if (_identifierToType.TryGetValue(identifier, out Type? existing) && existing != type)
            throw new InvalidOperationException(
                $"Cannot register '{type.FullName}' with identifier '{identifier}': " +
                $"it is already claimed by '{existing.FullName}'. " +
                $"Give one of the types a unique [MessageType(\"...\")] attribute.");

        _typeToIdentifier.TryAdd(type, identifier);
        _identifierToType.TryAdd(identifier, type);
    }

    /// <inheritdoc/>
    public string GetIdentifier(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (_typeToIdentifier.TryGetValue(type, out string? identifier))
            return identifier;

        // Type was never explicitly registered — fall back to FullName or attribute.
        return GetAttributeIdentifier(type) ?? type.FullName!;
    }

    /// <inheritdoc/>
    public Type Resolve(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        // 1. Registry lookup (covers [MessageType] aliases and registered FullNames).
        if (_identifierToType.TryGetValue(identifier, out Type? type))
            return type;

        // 2. Scan loaded assemblies by FullName (catches types not yet in the registry).
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? found = assembly.GetType(identifier);
            if (found is not null)
                return found;
        }

        // 3. Type.GetType — backwards-compat for old AssemblyQualifiedName rows.
        Type? legacy = Type.GetType(identifier);
        if (legacy is not null)
            return legacy;

        throw new InvalidOperationException(
            $"Cannot resolve message type '{identifier}'. Register the type via AddHandlersFromAssemblies " +
            $"or add a [MessageType(\"{identifier}\")] attribute to the new contract type.");
    }

    private static string? GetAttributeIdentifier(Type type)
    {
        var attr = (MessageTypeAttribute?)Attribute.GetCustomAttribute(type, typeof(MessageTypeAttribute));
        return attr?.Identifier;
    }
}
