using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpinionatedEventing.AzureServiceBus;
using OpinionatedEventing.AzureServiceBus.Routing;
using OpinionatedEventing.Attributes;
using OpinionatedEventing.Outbox;
using Reqnroll;

namespace OpinionatedEventing.AzureServiceBus.Specs.StepDefinitions;

[Binding]
public sealed class AzureServiceBusSteps
{
    private Type? _messageType;
    private string? _resolvedName;
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

    [Given("the Azure Service Bus transport is registered with a connection string")]
    public void GivenTransportRegisteredWithConnectionString()
    {
        _services = new ServiceCollection();
        _services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        _services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _services.AddOpinionatedEventing();
        _services.AddAzureServiceBusTransport(o =>
        {
            o.ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
            o.ServiceName = "test-service";
        });
    }

    [Given("the Azure Service Bus transport is registered with ServiceName {string}")]
    public void GivenTransportRegisteredWithServiceName(string serviceName)
    {
        _services = new ServiceCollection();
        _services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        _services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _services.AddOpinionatedEventing();
        _services.AddAzureServiceBusTransport(o =>
        {
            o.ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
            o.ServiceName = serviceName;
        });
    }

    // --- When ---

    [When("I resolve the topic name")]
    public void WhenIResolveTheTopicName()
    {
        _resolvedName = MessageNamingConvention.GetTopicName(_messageType!);
    }

    [When("I resolve the queue name")]
    public void WhenIResolveTheQueueName()
    {
        _resolvedName = MessageNamingConvention.GetQueueName(_messageType!);
    }

    [When("the service provider is built")]
    public void WhenServiceProviderIsBuilt()
    {
        _serviceProvider = _services!.BuildServiceProvider();
    }

    // --- Then ---

    [Then("the topic name is {string}")]
    public void ThenTheTopicNameIs(string expected)
    {
        Xunit.Assert.Equal(expected, _resolvedName);
    }

    [Then("the queue name is {string}")]
    public void ThenTheQueueNameIs(string expected)
    {
        Xunit.Assert.Equal(expected, _resolvedName);
    }

    [Then("ITransport is registered in the container")]
    public void ThenITransportIsRegistered()
    {
        Xunit.Assert.NotNull(_serviceProvider!.GetService<ITransport>());
    }

    [Then("the ServiceName option is {string}")]
    public void ThenServiceNameOptionIs(string expected)
    {
        var opts = _serviceProvider!.GetRequiredService<IOptions<AzureServiceBusOptions>>().Value;
        Xunit.Assert.Equal(expected, opts.ServiceName);
    }

    // --- message types ---

    private sealed record OrderPlaced(string OrderId = "") : IEvent;
    private sealed record ProcessPayment(string PaymentId = "", decimal Amount = 0) : ICommand;

    [MessageTopic("my-custom-topic")]
    private sealed record CustomTopicEvent : IEvent;
}
