#nullable enable

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MSOptions = Microsoft.Extensions.Options.Options;
using OpinionatedEventing;
using OpinionatedEventing.AzureServiceBus;
using OpinionatedEventing.DependencyInjection;
using Xunit;

namespace OpinionatedEventing.AzureServiceBus.Tests;

/// <summary>
/// Unit tests for the pause/resume loop in <see cref="AzureServiceBusConsumerWorker"/>.
/// These tests register no handlers so the worker never calls <see cref="ServiceBusClient"/>
/// — it proceeds directly to <c>RunPauseLoopAsync</c> where the pause logic lives.
/// </summary>
public sealed class ConsumerWorkerPauseTests
{
    private static AzureServiceBusConsumerWorker CreateWorker(IConsumerPauseController pauseController)
    {
        // Empty registry → no handlers registered → no processors created
        var options = MSOptions.Create(new AzureServiceBusOptions
        {
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;" +
                               "SharedAccessKeyName=RootManageSharedAccessKey;" +
                               "SharedAccessKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        });

        return new AzureServiceBusConsumerWorker(
            client: new NoOpServiceBusClient(),
            handlerRunner: new NeverCalledHandlerRunner(),
            scopeFactory: new NeverCalledScopeFactory(),
            registry: new MessageHandlerRegistry(),
            options: options,
            pauseController: pauseController,
            timeProvider: TimeProvider.System,
            logger: NullLogger<AzureServiceBusConsumerWorker>.Instance);
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

        // Transition to paused while running
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

    // ─── Minimal fakes ────────────────────────────────────────────────────────────

    /// <summary>
    /// Uses the protected parameterless constructor Azure SDK exposes for testing.
    /// No methods are called in no-handler tests.
    /// </summary>
    private sealed class NoOpServiceBusClient : ServiceBusClient { }

    private sealed class NeverCalledHandlerRunner : IMessageHandlerRunner
    {
        public Task RunAsync(string messageType, string messageKind, string payload,
            Guid? messageId, Guid correlationId, Guid? causationId, CancellationToken ct)
            => throw new InvalidOperationException("Should not be called in no-handler tests.");
    }

    private sealed class NeverCalledScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
            => throw new InvalidOperationException("Should not be called in no-handler tests.");
    }
}
