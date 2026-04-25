#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MSOptions = Microsoft.Extensions.Options.Options;
using OpinionatedEventing;
using OpinionatedEventing.RabbitMQ;
using OpinionatedEventing.RabbitMQ.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace OpinionatedEventing.RabbitMQ.Tests;

/// <summary>
/// Unit tests for the pause/resume loop in <see cref="RabbitMQConsumerWorker"/>.
/// These tests register no handlers so the worker never calls <see cref="IConnection"/>
/// — it proceeds directly to <c>RunPauseLoopAsync</c> where the pause logic lives.
/// </summary>
public sealed class ConsumerWorkerPauseTests
{
    private static RabbitMQConsumerWorker CreateWorker(IConsumerPauseController pauseController)
    {
        // Empty service collection → ScanHandlerTypes returns nothing → no channels created
        var emptyServices = new ServiceCollection();
        var accessor = new ServiceCollectionAccessor(emptyServices);
        var options = MSOptions.Create(new RabbitMQOptions { ConnectionString = "amqp://localhost" });

        return new RabbitMQConsumerWorker(
            connection: new NeverCalledConnection(),
            handlerRunner: new NeverCalledHandlerRunner(),
            scopeFactory: new NeverCalledScopeFactory(),
            accessor: accessor,
            options: options,
            pauseController: pauseController,
            timeProvider: TimeProvider.System,
            logger: NullLogger<RabbitMQConsumerWorker>.Instance);
    }

    [Fact]
    public async Task Worker_starts_and_stops_cleanly_when_not_paused()
    {
        var worker = CreateWorker(new FakeConsumerPauseController(startPaused: false));

        await ((IHostedService)worker).StartAsync(CancellationToken.None);
        await ((IHostedService)worker).StopAsync(CancellationToken.None);
        // No exception = clean shutdown via the else-branch OperationCanceledException path
    }

    [Fact]
    public async Task Worker_pauses_and_resumes_correctly()
    {
        var ct = TestContext.Current.CancellationToken;
        var pauseController = new FakeConsumerPauseController(startPaused: true);
        var worker = CreateWorker(pauseController);

        await ((IHostedService)worker).StartAsync(CancellationToken.None);

        // Give the worker time to enter the paused branch and reach WhenStateChangedAsync
        await Task.Delay(50, ct);

        // Signal resume — worker should log "resumed" and re-enter the loop
        pauseController.SetPaused(false);

        // Give it time to process the resume and re-enter the else-branch wait
        await Task.Delay(50, ct);

        await ((IHostedService)worker).StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Worker_pauses_when_controller_transitions_mid_run()
    {
        var ct = TestContext.Current.CancellationToken;
        var pauseController = new FakeConsumerPauseController(startPaused: false);
        var worker = CreateWorker(pauseController);

        await ((IHostedService)worker).StartAsync(CancellationToken.None);
        await Task.Delay(20, ct);

        // Transition to paused while running — worker wakes from else-branch and enters if-branch
        pauseController.SetPaused(true);
        await Task.Delay(50, ct);

        // Resume before stopping
        pauseController.SetPaused(false);
        await Task.Delay(20, ct);

        await ((IHostedService)worker).StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Worker_stops_cleanly_when_cancelled_while_paused()
    {
        // Simulates the host stopping during an active readiness failure — the worker
        // must exit via the OperationCanceledException path without hanging.
        var ct = TestContext.Current.CancellationToken;
        var pauseController = new FakeConsumerPauseController(startPaused: true);
        var worker = CreateWorker(pauseController);

        await ((IHostedService)worker).StartAsync(CancellationToken.None);

        // Give the worker time to enter the paused branch and block on WhenStateChangedAsync
        await Task.Delay(50, ct);

        // Stop without ever resuming — stoppingToken cancellation must unblock the worker
        await ((IHostedService)worker).StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Worker_stops_cleanly_after_rapid_pause_resume_cycle()
    {
        // Stress-tests the volatile flag + Interlocked.Exchange state machine to ensure
        // no stuck state survives multiple rapid transitions before shutdown.
        var ct = TestContext.Current.CancellationToken;
        var pauseController = new FakeConsumerPauseController(startPaused: false);
        var worker = CreateWorker(pauseController);

        await ((IHostedService)worker).StartAsync(CancellationToken.None);
        await Task.Delay(20, ct);

        pauseController.SetPaused(true);
        await Task.Delay(20, ct);
        pauseController.SetPaused(false);
        await Task.Delay(20, ct);
        pauseController.SetPaused(true);
        await Task.Delay(20, ct);
        pauseController.SetPaused(false);
        await Task.Delay(20, ct);

        await ((IHostedService)worker).StopAsync(CancellationToken.None);
    }

    // ─── Minimal fakes — none of these are ever called in no-handler tests ────────

    private sealed class NeverCalledHandlerRunner : IMessageHandlerRunner
    {
        public Task RunAsync(string messageType, string messageKind, string payload,
            Guid correlationId, Guid? causationId, CancellationToken ct)
            => throw new InvalidOperationException("Should not be called in no-handler tests.");
    }

    private sealed class NeverCalledScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
            => throw new InvalidOperationException("Should not be called in no-handler tests.");
    }

    private sealed class NeverCalledConnection : IConnection
    {
        public ushort ChannelMax => 0;
        public IDictionary<string, object?> ClientProperties => new Dictionary<string, object?>();
        public string? ClientProvidedName => null;
        public ShutdownEventArgs? CloseReason => null;
        public AmqpTcpEndpoint Endpoint => new("localhost");
        public uint FrameMax => 0;
        public TimeSpan Heartbeat => TimeSpan.Zero;
        public bool IsOpen => true;
        public IProtocol Protocol => throw new InvalidOperationException("Should not be called.");
        public IDictionary<string, object?> ServerProperties => new Dictionary<string, object?>();
        public IEnumerable<ShutdownReportEntry> ShutdownReport => Array.Empty<ShutdownReportEntry>();

        // INetworkConnection members
        public int LocalPort => 0;
        public int RemotePort => 0;

        public event AsyncEventHandler<CallbackExceptionEventArgs>? CallbackExceptionAsync { add { } remove { } }
        public event AsyncEventHandler<ShutdownEventArgs>? ConnectionShutdownAsync { add { } remove { } }
        public event AsyncEventHandler<AsyncEventArgs>? RecoverySucceededAsync { add { } remove { } }
        public event AsyncEventHandler<ConnectionRecoveryErrorEventArgs>? ConnectionRecoveryErrorAsync { add { } remove { } }
        public event AsyncEventHandler<ConsumerTagChangedAfterRecoveryEventArgs>? ConsumerTagChangeAfterRecoveryAsync { add { } remove { } }
        public event AsyncEventHandler<QueueNameChangedAfterRecoveryEventArgs>? QueueNameChangedAfterRecoveryAsync { add { } remove { } }
        public event AsyncEventHandler<RecoveringConsumerEventArgs>? RecoveringConsumerAsync { add { } remove { } }
        public event AsyncEventHandler<ConnectionBlockedEventArgs>? ConnectionBlockedAsync { add { } remove { } }
        public event AsyncEventHandler<AsyncEventArgs>? ConnectionUnblockedAsync { add { } remove { } }

        public Task CloseAsync(ushort reasonCode, string reasonText, TimeSpan timeout, bool abort,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IChannel> CreateChannelAsync(CreateChannelOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Should not be called in no-handler tests.");

        public Task UpdateSecretAsync(string newSecret, string reason,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }
}
