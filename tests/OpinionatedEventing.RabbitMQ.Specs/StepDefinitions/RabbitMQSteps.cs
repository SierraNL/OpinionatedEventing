#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Attributes;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.RabbitMQ;
using OpinionatedEventing.RabbitMQ.Routing;
using Reqnroll;

namespace OpinionatedEventing.RabbitMQ.Specs.StepDefinitions;

[Binding]
public sealed class RabbitMQSteps
{
    private Type? _messageType;
    private string? _resolvedName;
    private string? _serviceName;
    private IServiceCollection? _services;
    private IServiceProvider? _serviceProvider;

    // --- Given ---

    [Given("an event type named {string}")]
    public void GivenAnEventTypeNamed(string typeName)
    {
        _messageType = typeName switch
        {
            "OrderPlaced" => typeof(OrderPlaced),
            _ => throw new NotSupportedException($"Unknown type '{typeName}'.")
        };
    }

    [Given("an event type with a MessageTopic attribute set to {string}")]
    public void GivenAnEventTypeWithMessageTopicAttribute(string _)
    {
        _messageType = typeof(CustomTopicEvent);
    }

    [Given("a command type named {string}")]
    public void GivenACommandTypeNamed(string typeName)
    {
        _messageType = typeName switch
        {
            "ProcessPayment" => typeof(ProcessPayment),
            _ => throw new NotSupportedException($"Unknown type '{typeName}'.")
        };
    }

    [Given("a command type with a MessageQueue attribute set to {string}")]
    public void GivenACommandTypeWithMessageQueueAttribute(string _)
    {
        _messageType = typeof(CustomQueueCommand);
    }

    [Given("the service name is {string}")]
    public void GivenServiceNameIs(string serviceName)
    {
        _serviceName = serviceName;
    }

    [Given("the RabbitMQ transport is registered with a connection string")]
    public void GivenTransportRegisteredWithConnectionString()
    {
        _services = new ServiceCollection();
        _services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        _services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _services.AddOpinionatedEventing();
        _services.AddRabbitMQTransport(o =>
        {
            o.ConnectionString = "amqp://guest:guest@localhost:5672/";
            o.ServiceName = "test-service";
        });
    }

    [Given("the RabbitMQ transport is registered with ServiceName {string}")]
    public void GivenTransportRegisteredWithServiceName(string serviceName)
    {
        _services = new ServiceCollection();
        _services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        _services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _services.AddOpinionatedEventing();
        _services.AddRabbitMQTransport(o =>
        {
            o.ConnectionString = "amqp://guest:guest@localhost:5672/";
            o.ServiceName = serviceName;
        });
    }

    // --- When ---

    [When("I resolve the exchange name")]
    public void WhenIResolveTheExchangeName()
    {
        _resolvedName = MessageNamingConvention.GetExchangeName(_messageType!);
    }

    [When("I resolve the queue name")]
    public void WhenIResolveTheQueueName()
    {
        _resolvedName = MessageNamingConvention.GetQueueName(_messageType!);
    }

    [When("I resolve the event consumer queue name")]
    public void WhenIResolveTheEventConsumerQueueName()
    {
        _resolvedName = MessageNamingConvention.GetEventQueueName(_messageType!, _serviceName!);
    }

    [When("the service provider is built")]
    public void WhenServiceProviderIsBuilt()
    {
        _serviceProvider = _services!.BuildServiceProvider();
    }

    // --- Then ---

    [Then("the exchange name is {string}")]
    public void ThenTheExchangeNameIs(string expected)
    {
        Xunit.Assert.Equal(expected, _resolvedName);
    }

    [Then("the queue name is {string}")]
    public void ThenTheQueueNameIs(string expected)
    {
        Xunit.Assert.Equal(expected, _resolvedName);
    }

    [Then("the event queue name is {string}")]
    public void ThenTheEventQueueNameIs(string expected)
    {
        Xunit.Assert.Equal(expected, _resolvedName);
    }

    [Then("ITransport is registered in the service collection")]
    public void ThenITransportIsRegisteredInServiceCollection()
    {
        Xunit.Assert.Contains(_services!, d => d.ServiceType == typeof(ITransport));
    }

    [Then("the ServiceName option is {string}")]
    public void ThenServiceNameOptionIs(string expected)
    {
        var opts = _serviceProvider!.GetRequiredService<IOptions<RabbitMQOptions>>().Value;
        Xunit.Assert.Equal(expected, opts.ServiceName);
    }

    // --- message types ---

    private sealed record OrderPlaced(string OrderId = "") : IEvent;
    private sealed record ProcessPayment(string PaymentId = "") : ICommand;

    [MessageTopic("my-custom-exchange")]
    private sealed record CustomTopicEvent : IEvent;

    [MessageQueue("payments")]
    private sealed record CustomQueueCommand : ICommand;
}
