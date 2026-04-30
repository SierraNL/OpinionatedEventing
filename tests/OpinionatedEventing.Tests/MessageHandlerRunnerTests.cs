using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Tests;

public sealed class MessageHandlerRunnerTests
{
    private sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider? _scopeProvider;
        public List<CapturedLogEntry> Entries { get; } = [];

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries, () => _scopeProvider);
        public void Dispose() { }
    }

    private sealed class CapturingLogger(
        string category,
        List<CapturedLogEntry> entries,
        Func<IExternalScopeProvider?> getScopeProvider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => getScopeProvider()?.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopes = new Dictionary<string, object?>();
            getScopeProvider()?.ForEachScope((scope, acc) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> kvps)
                    foreach (var kvp in kvps) acc[kvp.Key] = kvp.Value;
            }, scopes);
            entries.Add(new CapturedLogEntry(category, scopes));
        }
    }

    private sealed record CapturedLogEntry(string Category, Dictionary<string, object?> Scopes);

    private sealed class LoggingEventHandler(ILogger<LoggingEventHandler> logger) : IEventHandler<RunnerEvent>
    {
        public Task HandleAsync(RunnerEvent @event, CancellationToken ct)
        {
            logger.LogInformation("Handling event {EventId}", @event.Id);
            return Task.CompletedTask;
        }
    }

    private sealed record RunnerEvent(Guid Id) : IEvent;
    private sealed record RunnerCommand(Guid Id) : ICommand;

    // Captures the event payload and the ambient IMessagingContext values at call time.
    private sealed class RecordingEventHandler : IEventHandler<RunnerEvent>
    {
        private readonly IMessagingContext _ctx;
        private readonly List<(RunnerEvent Event, Guid MessageId, Guid CorrelationId, Guid? CausationId)> _log;

        public RecordingEventHandler(
            IMessagingContext ctx,
            List<(RunnerEvent, Guid, Guid, Guid?)> log)
        {
            _ctx = ctx;
            _log = log;
        }

        public Task HandleAsync(RunnerEvent @event, CancellationToken cancellationToken)
        {
            _log.Add((@event, _ctx.MessageId, _ctx.CorrelationId, _ctx.CausationId));
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

    private static ServiceProvider BuildProvider(
        Action<IServiceCollection>? configure = null,
        ILoggerFactory? loggerFactory = null)
    {
        var services = new ServiceCollection();
        if (loggerFactory is not null)
        {
            services.AddSingleton(loggerFactory);
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        }
        else
        {
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        }
        services.AddOpinionatedEventing();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunAsync_event_dispatches_payload_to_handler()
    {
        var log = new List<(RunnerEvent, Guid, Guid, Guid?)>();
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
            null,
            Guid.NewGuid(),
            null,
            ct);

        Assert.Single(log);
        Assert.Equal(ev.Id, log[0].Item1.Id);
    }

    [Fact]
    public async Task RunAsync_event_initialises_messaging_context()
    {
        var log = new List<(RunnerEvent, Guid, Guid, Guid?)>();
        var ct = TestContext.Current.CancellationToken;

        await using var provider = BuildProvider(s =>
            s.AddScoped<IEventHandler<RunnerEvent>>(sp =>
                new RecordingEventHandler(sp.GetRequiredService<IMessagingContext>(), log)));

        var runner = provider.GetRequiredService<IMessageHandlerRunner>();
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        await runner.RunAsync(
            typeof(RunnerEvent).AssemblyQualifiedName!,
            "Event",
            JsonSerializer.Serialize(new RunnerEvent(Guid.NewGuid())),
            messageId,
            correlationId,
            causationId,
            ct);

        Assert.Equal(messageId, log[0].Item2);
        Assert.Equal(correlationId, log[0].Item3);
        Assert.Equal(causationId, log[0].Item4);
    }

    [Fact]
    public async Task RunAsync_null_message_id_generates_a_random_message_id()
    {
        var log = new List<(RunnerEvent, Guid, Guid, Guid?)>();
        var ct = TestContext.Current.CancellationToken;

        await using var provider = BuildProvider(s =>
            s.AddScoped<IEventHandler<RunnerEvent>>(sp =>
                new RecordingEventHandler(sp.GetRequiredService<IMessagingContext>(), log)));

        var runner = provider.GetRequiredService<IMessageHandlerRunner>();

        await runner.RunAsync(
            typeof(RunnerEvent).AssemblyQualifiedName!,
            "Event",
            JsonSerializer.Serialize(new RunnerEvent(Guid.NewGuid())),
            null,
            Guid.NewGuid(),
            null,
            ct);

        Assert.NotEqual(Guid.Empty, log[0].Item2);
    }

    [Fact]
    public async Task RunAsync_event_causation_id_is_null_for_originating_messages()
    {
        var log = new List<(RunnerEvent, Guid, Guid, Guid?)>();
        var ct = TestContext.Current.CancellationToken;

        await using var provider = BuildProvider(s =>
            s.AddScoped<IEventHandler<RunnerEvent>>(sp =>
                new RecordingEventHandler(sp.GetRequiredService<IMessagingContext>(), log)));

        var runner = provider.GetRequiredService<IMessageHandlerRunner>();

        await runner.RunAsync(
            typeof(RunnerEvent).AssemblyQualifiedName!,
            "Event",
            JsonSerializer.Serialize(new RunnerEvent(Guid.NewGuid())),
            null,
            Guid.NewGuid(),
            null,
            ct);

        Assert.Null(log[0].Item4);
    }

    [Fact]
    public async Task RunAsync_event_dispatches_to_all_registered_handlers()
    {
        var log = new List<(RunnerEvent, Guid, Guid, Guid?)>();
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
            null,
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
            null,
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
            null,
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
                null,
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
                null,
                Guid.NewGuid(),
                null,
                ct));
    }

    [Fact]
    public async Task RunAsync_exposes_message_id_correlation_causation_and_message_type_in_logging_scope()
    {
        var ct = TestContext.Current.CancellationToken;
        var capturingProvider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(capturingProvider));

        await using var provider = BuildProvider(
            s => s.AddScoped<IEventHandler<RunnerEvent>>(
                sp => new LoggingEventHandler(sp.GetRequiredService<ILogger<LoggingEventHandler>>())),
            loggerFactory);

        var runner = provider.GetRequiredService<IMessageHandlerRunner>();
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var messageType = typeof(RunnerEvent).AssemblyQualifiedName!;

        await runner.RunAsync(
            messageType, "Event",
            JsonSerializer.Serialize(new RunnerEvent(Guid.NewGuid())),
            messageId, correlationId, causationId, ct);

        var handlerEntry = capturingProvider.Entries
            .Single(e => e.Category.Contains(nameof(LoggingEventHandler)));

        Assert.Equal(messageId, handlerEntry.Scopes["MessageId"]);
        Assert.Equal(correlationId, handlerEntry.Scopes["CorrelationId"]);
        Assert.Equal(causationId, handlerEntry.Scopes["CausationId"]);
        Assert.Equal(messageType, handlerEntry.Scopes["MessageType"]);
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
                var log = new List<(RunnerEvent, Guid, Guid, Guid?)>();
                return new RecordingEventHandler(sp.GetRequiredService<IMessagingContext>(), log);
            }));

        var runner = provider.GetRequiredService<IMessageHandlerRunner>();
        var payload = JsonSerializer.Serialize(new RunnerEvent(Guid.NewGuid()));

        await runner.RunAsync(typeof(RunnerEvent).AssemblyQualifiedName!, "Event", payload, null, Guid.NewGuid(), null, ct);
        await runner.RunAsync(typeof(RunnerEvent).AssemblyQualifiedName!, "Event", payload, null, Guid.NewGuid(), null, ct);

        Assert.Equal(2, capturedContexts.Count);
        Assert.NotSame(capturedContexts[0], capturedContexts[1]);
    }

    // ---- causation chain test ----

    private sealed record ChainEvent : IEvent;

    private sealed class PublishingChainHandler(IPublisher publisher) : IEventHandler<ChainEvent>
    {
        public Task HandleAsync(ChainEvent @event, CancellationToken ct) =>
            publisher.PublishEventAsync(new ChainEvent(), ct);
    }

    [Fact]
    public async Task RunAsync_causation_chain_threads_inbound_message_id_as_causation_id()
    {
        // Verifies the A → B → C causation chain promised by the docs.
        // The transport passes the inbound message's own Id as both messageId and causationId;
        // any message published inside the handler must carry that value as its CausationId.
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryOutboxStore();
        var correlationId = Guid.NewGuid();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddOpinionatedEventing().AddOutbox();
        services.AddSingleton<IOutboxStore>(store);
        services.AddScoped<IEventHandler<ChainEvent>>(sp =>
            new PublishingChainHandler(sp.GetRequiredService<IPublisher>()));
        await using var provider = services.BuildServiceProvider();

        var runner = provider.GetRequiredService<IMessageHandlerRunner>();
        var messageAId = Guid.NewGuid();

        // Process A — transport passes A's own MessageId
        await runner.RunAsync(
            typeof(ChainEvent).AssemblyQualifiedName!, "Event",
            JsonSerializer.Serialize(new ChainEvent()),
            messageAId, correlationId, messageAId, ct);

        var messageB = Assert.Single(store.Messages);
        Assert.Equal(messageAId, messageB.CausationId);

        // Process B — transport passes B.Id (its MessageId on the wire)
        await runner.RunAsync(
            typeof(ChainEvent).AssemblyQualifiedName!, "Event",
            JsonSerializer.Serialize(new ChainEvent()),
            messageB.Id, correlationId, messageB.Id, ct);

        Assert.Equal(2, store.Messages.Count);
        var messageC = store.Messages.Single(m => m.Id != messageB.Id);
        Assert.Equal(messageB.Id, messageC.CausationId);
    }
}
