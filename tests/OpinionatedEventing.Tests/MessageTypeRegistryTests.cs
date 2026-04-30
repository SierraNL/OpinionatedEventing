#nullable enable

using OpinionatedEventing.Attributes;
using Xunit;

namespace OpinionatedEventing.Tests;

public sealed class MessageTypeRegistryTests
{
    [Fact]
    public void GetIdentifier_returns_FullName_for_undecorated_type()
    {
        var registry = new MessageTypeRegistry();
        registry.Register(typeof(UndecoratedMessage));

        Assert.Equal("OpinionatedEventing.Tests.MessageTypeRegistryTests+UndecoratedMessage",
            registry.GetIdentifier(typeof(UndecoratedMessage)));
    }

    [Fact]
    public void GetIdentifier_returns_attribute_identifier_for_decorated_type()
    {
        var registry = new MessageTypeRegistry();
        registry.Register(typeof(DecoratedMessage));

        Assert.Equal("tests.decorated-message", registry.GetIdentifier(typeof(DecoratedMessage)));
    }

    [Fact]
    public void Resolve_finds_type_by_FullName()
    {
        var registry = new MessageTypeRegistry();
        registry.Register(typeof(UndecoratedMessage));

        var resolved = registry.Resolve("OpinionatedEventing.Tests.MessageTypeRegistryTests+UndecoratedMessage");

        Assert.Equal(typeof(UndecoratedMessage), resolved);
    }

    [Fact]
    public void Resolve_finds_type_by_attribute_identifier()
    {
        var registry = new MessageTypeRegistry();
        registry.Register(typeof(DecoratedMessage));

        var resolved = registry.Resolve("tests.decorated-message");

        Assert.Equal(typeof(DecoratedMessage), resolved);
    }

    [Fact]
    public void Roundtrip_namespace_rename_via_MessageType_attribute()
    {
        // Simulates: type was published as "old.namespace.OrderPlaced", namespace got renamed,
        // but the contract record carries [MessageType("old.namespace.OrderPlaced")] so old
        // outbox rows still resolve to the new CLR type.
        var registry = new MessageTypeRegistry();
        registry.Register(typeof(RenamedOrderPlaced));

        var resolvedByOldIdentifier = registry.Resolve("old.namespace.OrderPlaced");
        var currentIdentifier = registry.GetIdentifier(typeof(RenamedOrderPlaced));

        Assert.Equal(typeof(RenamedOrderPlaced), resolvedByOldIdentifier);
        Assert.Equal("old.namespace.OrderPlaced", currentIdentifier);
    }

    [Fact]
    public void Resolve_throws_for_unknown_identifier()
    {
        var registry = new MessageTypeRegistry();
        // Use a GUID-embedded string guaranteed not to match any loaded assembly type.
        var unknownId = $"no.such.assembly.Type_{Guid.NewGuid():N}";

        Assert.Throws<InvalidOperationException>(() => registry.Resolve(unknownId));
    }

    [Fact]
    public void GetIdentifier_falls_back_to_FullName_for_unregistered_type()
    {
        var registry = new MessageTypeRegistry();

        // No explicit Register call — should still return FullName.
        Assert.Equal("OpinionatedEventing.Tests.MessageTypeRegistryTests+UndecoratedMessage",
            registry.GetIdentifier(typeof(UndecoratedMessage)));
    }

    [Fact]
    public void Register_throws_when_two_types_claim_the_same_identifier()
    {
        var registry = new MessageTypeRegistry();
        registry.Register(typeof(DecoratedMessage)); // identifier: "tests.decorated-message"

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(typeof(CollidingMessage)));
    }

    [Fact]
    public void Register_is_idempotent()
    {
        var registry = new MessageTypeRegistry();
        registry.Register(typeof(UndecoratedMessage));
        registry.Register(typeof(UndecoratedMessage)); // second call must not throw

        Assert.Equal("OpinionatedEventing.Tests.MessageTypeRegistryTests+UndecoratedMessage",
            registry.GetIdentifier(typeof(UndecoratedMessage)));
    }

    // ── inner contract types ──────────────────────────────────────────────────

    private sealed record UndecoratedMessage(Guid Id) : IEvent;

    [MessageType("tests.decorated-message")]
    private sealed record DecoratedMessage(Guid Id) : IEvent;

    [MessageType("old.namespace.OrderPlaced")]
    private sealed record RenamedOrderPlaced(Guid OrderId) : IEvent;

    [MessageType("tests.decorated-message")] // intentional clash with DecoratedMessage
    private sealed record CollidingMessage(Guid Id) : IEvent;
}
