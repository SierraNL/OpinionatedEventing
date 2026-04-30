#nullable enable

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MSOptions = Microsoft.Extensions.Options.Options;
using OpinionatedEventing;
using OpinionatedEventing.AzureServiceBus;
using OpinionatedEventing.AzureServiceBus.DependencyInjection;
using Xunit;

namespace OpinionatedEventing.AzureServiceBus.Tests;

public sealed class AzureServiceBusConsumerWorkerTests
{
    private static AzureServiceBusConsumerWorker CreateWorker(IMessageHandlerRunner runner)
    {
        var emptyServices = new ServiceCollection();
        var accessor = new ServiceCollectionAccessor(emptyServices);
        var options = MSOptions.Create(new AzureServiceBusOptions
        {
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;" +
                               "SharedAccessKeyName=RootManageSharedAccessKey;" +
                               "SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        });

        return new AzureServiceBusConsumerWorker(
            client: new NoOpServiceBusClient(),
            handlerRunner: runner,
            scopeFactory: new NeverCalledScopeFactory(),
            accessor: accessor,
            options: options,
            pauseController: new FakeConsumerPauseController(startPaused: false),
            timeProvider: TimeProvider.System,
            logger: NullLogger<AzureServiceBusConsumerWorker>.Instance);
    }

    [Fact]
    public async Task ProcessReceivedMessageAsync_passes_inbound_MessageId_as_causationId()
    {
        var ct = TestContext.Current.CancellationToken;
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var runner = new RecordingHandlerRunner();
        var worker = CreateWorker(runner);

        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: messageId.ToString(),
            properties: new Dictionary<string, object>
            {
                ["MessageType"] = typeof(object).AssemblyQualifiedName!,
                ["MessageKind"] = "Event",
                ["CorrelationId"] = correlationId.ToString(),
            });

        await worker.ProcessReceivedMessageAsync(
            message,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _, _) => Task.CompletedTask,
            ct);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(messageId, call.CausationId);
        Assert.Equal(correlationId, call.CorrelationId);
    }

    [Fact]
    public async Task ProcessReceivedMessageAsync_causationId_is_null_when_MessageId_is_not_a_guid()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.NewGuid();
        var runner = new RecordingHandlerRunner();
        var worker = CreateWorker(runner);

        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"),
            messageId: "not-a-guid",
            properties: new Dictionary<string, object>
            {
                ["MessageType"] = typeof(object).AssemblyQualifiedName!,
                ["MessageKind"] = "Event",
                ["CorrelationId"] = correlationId.ToString(),
            });

        await worker.ProcessReceivedMessageAsync(
            message,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _, _) => Task.CompletedTask,
            ct);

        var call = Assert.Single(runner.Calls);
        Assert.Null(call.CausationId);
    }

    [Fact]
    public async Task ProcessReceivedMessageAsync_dead_letters_message_missing_required_properties()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new RecordingHandlerRunner();
        var worker = CreateWorker(runner);
        var deadLettered = false;

        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString("{}"));

        await worker.ProcessReceivedMessageAsync(
            message,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _, _) => { deadLettered = true; return Task.CompletedTask; },
            ct);

        Assert.True(deadLettered);
        Assert.Empty(runner.Calls);
    }

    // ─── Fakes ────────────────────────────────────────────────────────────────────

    private sealed class NoOpServiceBusClient : ServiceBusClient { }

    private sealed class NeverCalledScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
            => throw new InvalidOperationException("Should not be called.");
    }

    private sealed record RunnerCall(
        string MessageType, string MessageKind, string Payload,
        Guid CorrelationId, Guid? CausationId);

    private sealed class RecordingHandlerRunner : IMessageHandlerRunner
    {
        public List<RunnerCall> Calls { get; } = [];

        public Task RunAsync(string messageType, string messageKind, string payload,
            Guid? messageId, Guid correlationId, Guid? causationId, CancellationToken ct)
        {
            Calls.Add(new RunnerCall(messageType, messageKind, payload, correlationId, causationId));
            return Task.CompletedTask;
        }
    }
}
