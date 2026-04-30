using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Tests;

public sealed class FakeMessagingContextTests
{
    [Fact]
    public void FakeMessagingContext_HasDefaultMessageId()
    {
        var ctx = new FakeMessagingContext();
        Assert.NotEqual(Guid.Empty, ctx.MessageId);
    }

    [Fact]
    public void FakeMessagingContext_AllowsFixedMessageId()
    {
        var id = Guid.NewGuid();
        var ctx = new FakeMessagingContext { MessageId = id };
        Assert.Equal(id, ctx.MessageId);
    }

    [Fact]
    public void FakeMessagingContext_HasDefaultCorrelationId()
    {
        var ctx = new FakeMessagingContext();
        Assert.NotEqual(Guid.Empty, ctx.CorrelationId);
    }

    [Fact]
    public void FakeMessagingContext_DefaultCausationIdIsNull()
    {
        var ctx = new FakeMessagingContext();
        Assert.Null(ctx.CausationId);
    }

    [Fact]
    public void FakeMessagingContext_AllowsFixedCorrelationId()
    {
        var id = Guid.NewGuid();
        var ctx = new FakeMessagingContext { CorrelationId = id };
        Assert.Equal(id, ctx.CorrelationId);
    }

    [Fact]
    public void FakeMessagingContext_AllowsFixedCausationId()
    {
        var id = Guid.NewGuid();
        var ctx = new FakeMessagingContext { CausationId = id };
        Assert.Equal(id, ctx.CausationId);
    }

    [Fact]
    public void FakeMessagingContext_ImplementsIMessagingContext()
    {
        Assert.IsAssignableFrom<IMessagingContext>(new FakeMessagingContext());
    }
}
