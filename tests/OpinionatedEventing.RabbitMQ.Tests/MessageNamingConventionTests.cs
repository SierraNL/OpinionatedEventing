#nullable enable

using OpinionatedEventing.Attributes;
using OpinionatedEventing.RabbitMQ.Routing;
using Xunit;

namespace OpinionatedEventing.RabbitMQ.Tests;

public sealed class MessageNamingConventionTests
{
    [Theory]
    [InlineData(typeof(OrderPlaced), "order-placed")]
    [InlineData(typeof(PaymentReceived), "payment-received")]
    [InlineData(typeof(SimpleEvent), "simple-event")]
    [InlineData(typeof(ABCEvent), "abc-event")]
    [InlineData(typeof(HTTPRequestEvent), "http-request-event")]
    public void GetExchangeName_derives_kebab_case_from_type_name(Type type, string expected)
    {
        Assert.Equal(expected, MessageNamingConvention.GetExchangeName(type));
    }

    [Fact]
    public void GetExchangeName_uses_attribute_when_present()
    {
        Assert.Equal("my-custom-topic", MessageNamingConvention.GetExchangeName(typeof(CustomTopicEvent)));
    }

    [Theory]
    [InlineData(typeof(ProcessPayment), "process-payment")]
    [InlineData(typeof(CancelOrder), "cancel-order")]
    public void GetQueueName_derives_kebab_case_from_type_name(Type type, string expected)
    {
        Assert.Equal(expected, MessageNamingConvention.GetQueueName(type));
    }

    [Fact]
    public void GetQueueName_uses_attribute_when_present()
    {
        Assert.Equal("my-custom-queue", MessageNamingConvention.GetQueueName(typeof(CustomQueueCommand)));
    }

    [Fact]
    public void GetEventQueueName_prefixes_with_service_name()
    {
        Assert.Equal("my-service.order-placed",
            MessageNamingConvention.GetEventQueueName(typeof(OrderPlaced), "my-service"));
    }

    // --- test types ---

    private sealed record OrderPlaced : IEvent;
    private sealed record PaymentReceived : IEvent;
    private sealed record SimpleEvent : IEvent;
    private sealed record ABCEvent : IEvent;
    private sealed record HTTPRequestEvent : IEvent;

    [MessageTopic("my-custom-topic")]
    private sealed record CustomTopicEvent : IEvent;

    private sealed record ProcessPayment : ICommand;
    private sealed record CancelOrder : ICommand;

    [MessageQueue("my-custom-queue")]
    private sealed record CustomQueueCommand : ICommand;
}
