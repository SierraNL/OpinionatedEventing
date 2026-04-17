using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace OpinionatedEventing.Tests;

public sealed class MessageHandlerRunnerTests
{
    private sealed record RunnerEvent(Guid Id) : IEvent;
    private sealed record RunnerCommand(Guid Id) : ICommand;

    // Captures the event payload and the ambient IMessagingContext values at call time.
    private sealed class RecordingEventHandler : IEventHandler<RunnerEvent>
    {
        private readonly IMessagingContext _ctx;
        private readonly List<(RunnerEvent Event, Guid CorrelationId, Guid? CausationId)> _log;

        public RecordingEventHandler(
            IMessagingContext ctx,
            List<(RunnerEvent, Guid, Guid?)> log)
        {
            _ctx = ctx;
            _log = log;
        }

        public Task HandleAsync(RunnerEvent @event, CancellationToken cancellationToken)
        {
            _log.Add((@event, _ctx.CorrelationId, _ctx.CausationId));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCommandHandler : ICommandHandler<RunnerCommand>
    {
        private readonly IMessagingContext _ctx;
        private readonly List<(RunnerCommand Command, Guid CorrelationId, Guid? CausationId)> _log;

        public RecordingCommandHandler(
            IMessagingContext ctx,
            List<(RunnerCommand, Guid, Guid?)> log)
        {
            _ctx = ctx;
            _log = log;
        }

        public Task HandleAsync(RunnerCommand command, CancellationToken cancellationToken)
        {
            _log.Add((command, _ctx.CorrelationId, _ctx.CausationId));
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddOpinionatedEventing();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunAsync_event_dispatches_payload_to_handler()
    {
        var log = new List<(RunnerEvent, Guid, Guid?)>();
        var ct = TestContext.Current.CancellationToken;

        await using var provider = BuildProvider(s =>
            s.AddScoped<IEventHandler<RunnerEvent>>(sp =>
                new RecordingEventHandler(sp.GetRequiredService<IMessagingContext>(), log)));

        var runner = provider.GetRequiredService<IMessageHandlerRunner>();
        var ev = new RunnerEvent(Guid.NewGuid());

        await runner.RunAsync(
            typeof(RunnerEvent).AssemblyQualifiedName!,
            "Event",
            JsonSerializer.Serialize(ev),
            Guid.NewGuid(),
            null,
            ct);

        Assert.Single(log);
        Assert.Equal(ev.Id, log[0].Item1.Id);
    }

    [Fact]
    public async Task RunAsync_event_initialises_correlation_id_on_messaging_context()
    {
        var log = new List<(RunnerEvent, Guid, Guid?)>();
        var ct = TestContext.Current.CancellationToken;

        await using var provider = BuildProvider(s =>
            s.AddScoped<IEventHandler<RunnerEvent>>(sp =>
                new RecordingEventHandler(sp.GetRequiredService<IMessagingContext>(), log)));

        var runner = provider.GetRequiredService<IMessageHandlerRunner>();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        await runner.RunAsync(
            typeof(RunnerEvent).AssemblyQualifiedName!,
            "Event",
            JsonSerializer.Serialize(new RunnerEvent(Guid.NewGuid())),
            correlationId,
            causationId,
            ct);

        Assert.Equal(correlationId, log[0].Item2);
        Assert.Equal(causationId, log[0].Item3);
    }

    [Fact]
    public async Task RunAsync_event_causation_id_is_null_for_originating_messages()
    {
        var log = new List<(RunnerEvent, Guid, Guid?)>();
        var ct = TestContext.Current.CancellationToken;

        await using var provider = BuildProvider(s =>
            s.AddScoped<IEventHandler<RunnerEvent>>(sp =>
                new RecordingEventHandler(sp.GetRequiredService<IMessagingContext>(), log)));

        var runner = provider.GetRequiredService<IMessageHandlerRunner>();

        await runner.RunAsync(
            typeof(RunnerEvent).AssemblyQualifiedName!,
            "Event",
            JsonSerializer.Serialize(new RunnerEvent(Guid.NewGuid())),
            Guid.NewGuid(),
            null,
            ct);

        Assert.Null(log[0].Item3);
    }

    [Fact]
    public async Task RunAsync_event_dispatches_to_all_registered_handlers()
    {
        var log = new List<(RunnerEvent, Guid, Guid?)>();
        var ct = TestContext.Current.CancellationToken;

        await using var provider = BuildProvider(s =>
        {
            s.AddScoped<IEventHandler<RunnerEvent>>(sp =>
                new RecordingEventHandler(sp.GetRequiredService<IMessagingContext>(), log));
            s.AddScoped<IEventHandler<RunnerEvent>>(sp =>
                new RecordingEventHandler(sp.GetRequiredService<IMessagingContext>(), log));
        });

        var runner = provider.GetRequiredService<IMessageHandlerRunner>();

        await runner.RunAsync(
            typeof(RunnerEvent).AssemblyQualifiedName!,
            "Event",
            JsonSerializer.Serialize(new RunnerEvent(Guid.NewGuid())),
            Guid.NewGuid(),
            null,
            ct);

        Assert.Equal(2, log.Count);
    }

    [Fact]
    public async Task RunAsync_event_with_no_handlers_does_not_throw()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var runner = provider.GetRequiredService<IMessageHandlerRunner>();

        await runner.RunAsync(
            typeof(RunnerEvent).AssemblyQualifiedName!,
            "Event",
            JsonSerializer.Serialize(new RunnerEvent(Guid.NewGuid())),
            Guid.NewGuid(),
            null,
            ct);
    }

    [Fact]
    public async Task RunAsync_command_dispatches_payload_to_handler()
    {
        var log = new List<(RunnerCommand, Guid, Guid?)>();
        var ct = TestContext.Current.CancellationToken;

        await using var provider = BuildProvider(s =>
            s.AddScoped<ICommandHandler<RunnerCommand>>(sp =>
                new RecordingCommandHandler(sp.GetRequiredService<IMessagingContext>(), log)));

        var runner = provider.GetRequiredService<IMessageHandlerRunner>();
        var cmd = new RunnerCommand(Guid.NewGuid());
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        await runner.RunAsync(
            typeof(RunnerCommand).AssemblyQualifiedName!,
            "Command",
            JsonSerializer.Serialize(cmd),
            correlationId,
            causationId,
            ct);

        Assert.Single(log);
        Assert.Equal(cmd.Id, log[0].Item1.Id);
        Assert.Equal(correlationId, log[0].Item2);
        Assert.Equal(causationId, log[0].Item3);
    }

    [Fact]
    public async Task RunAsync_throws_on_unknown_message_kind()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var runner = provider.GetRequiredService<IMessageHandlerRunner>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(
                typeof(RunnerEvent).AssemblyQualifiedName!,
                "Unknown",
                JsonSerializer.Serialize(new RunnerEvent(Guid.NewGuid())),
                Guid.NewGuid(),
                null,
                ct));
    }

    [Fact]
    public async Task RunAsync_throws_on_unresolvable_message_type()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider();
        var runner = provider.GetRequiredService<IMessageHandlerRunner>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(
                "NonExistent.Type, NonExistent.Assembly",
                "Event",
                "{}",
                Guid.NewGuid(),
                null,
                ct));
    }

    [Fact]
    public async Task RunAsync_each_dispatch_gets_its_own_scope()
    {
        // Two sequential dispatches must not share the same MessagingContext instance.
        var capturedContexts = new List<IMessagingContext>();
        var ct = TestContext.Current.CancellationToken;

        await using var provider = BuildProvider(s =>
            s.AddScoped<IEventHandler<RunnerEvent>>(sp =>
            {
                capturedContexts.Add(sp.GetRequiredService<IMessagingContext>());
                var log = new List<(RunnerEvent, Guid, Guid?)>();
                return new RecordingEventHandler(sp.GetRequiredService<IMessagingContext>(), log);
            }));

        var runner = provider.GetRequiredService<IMessageHandlerRunner>();
        var payload = JsonSerializer.Serialize(new RunnerEvent(Guid.NewGuid()));

        await runner.RunAsync(typeof(RunnerEvent).AssemblyQualifiedName!, "Event", payload, Guid.NewGuid(), null, ct);
        await runner.RunAsync(typeof(RunnerEvent).AssemblyQualifiedName!, "Event", payload, Guid.NewGuid(), null, ct);

        Assert.Equal(2, capturedContexts.Count);
        Assert.NotSame(capturedContexts[0], capturedContexts[1]);
    }
}
