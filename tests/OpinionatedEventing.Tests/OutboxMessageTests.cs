using OpinionatedEventing.Outbox;
using Xunit;

namespace OpinionatedEventing.Tests;

public sealed class OutboxMessageTests
{
    [Fact]
    public void OutboxMessage_InitProperties_ArePreserved()
    {
        var id = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var message = new OutboxMessage
        {
            Id = id,
            MessageType = "MyApp.OrderPlaced, MyApp",
            Payload = "{}",
            MessageKind = "Event",
            CorrelationId = correlationId,
            CausationId = causationId,
            CreatedAt = now
        };

        Assert.Equal(id, message.Id);
        Assert.Equal("MyApp.OrderPlaced, MyApp", message.MessageType);
        Assert.Equal("{}", message.Payload);
        Assert.Equal("Event", message.MessageKind);
        Assert.Equal(correlationId, message.CorrelationId);
        Assert.Equal(causationId, message.CausationId);
        Assert.Equal(now, message.CreatedAt);
        Assert.Null(message.ProcessedAt);
        Assert.Null(message.FailedAt);
        Assert.Equal(0, message.AttemptCount);
        Assert.Null(message.Error);
    }

    [Fact]
    public void OutboxMessage_MutableProperties_CanBeUpdated()
    {
        var message = new OutboxMessage
        {
            MessageType = "T",
            Payload = "{}",
            MessageKind = "Command"
        };

        var processedAt = DateTimeOffset.UtcNow;
        message.ProcessedAt = processedAt;
        message.AttemptCount = 3;
        message.Error = "timeout";

        Assert.Equal(processedAt, message.ProcessedAt);
        Assert.Equal(3, message.AttemptCount);
        Assert.Equal("timeout", message.Error);
    }

    [Fact]
    public void OutboxMessage_CausationId_IsNullableByDefault()
    {
        var message = new OutboxMessage
        {
            MessageType = "T",
            Payload = "{}",
            MessageKind = "Event"
        };

        Assert.Null(message.CausationId);
    }
}
