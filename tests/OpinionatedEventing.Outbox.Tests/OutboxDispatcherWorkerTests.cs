#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Options;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Outbox.Tests;

public sealed class OutboxDispatcherWorkerTests
{
    // ---- helpers ----

    private static OutboxMessage MakePendingMessage(int attemptCount = 0) => new()
    {
        Id = Guid.NewGuid(),
        MessageType = "Test.Event, Test",
        Payload = "{}",
        MessageKind = "Event",
        CorrelationId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
        AttemptCount = attemptCount,
    };

    private static (OutboxDispatcherWorker Worker, InMemoryOutboxStore Store, FakeTransport Transport)
        BuildWorker(Action<OutboxOptions>? configureOutbox = null)
    {
        var store = new InMemoryOutboxStore();
        var transport = new FakeTransport();

        var optionsValue = new OpinionatedEventingOptions();
        optionsValue.Outbox.PollInterval = TimeSpan.Zero;
        configureOutbox?.Invoke(optionsValue.Outbox);

        var services = new ServiceCollection();
        services.AddSingleton<IOutboxStore>(store);
        services.AddSingleton<ITransport>(transport);
        var provider = services.BuildServiceProvider();

        var worker = new OutboxDispatcherWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(optionsValue),
            TimeProvider.System,
            NullLogger<OutboxDispatcherWorker>.Instance);

        return (worker, store, transport);
    }

    private static async Task RunWorkerUntilEmptyAsync(
        OutboxDispatcherWorker worker,
        IOutboxStore store,
        CancellationToken ct)
    {
        var task = worker.StartAsync(ct);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var pending = await store.GetPendingAsync(1, ct);
            if (pending.Count == 0)
                break;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await worker.StopAsync(CancellationToken.None);
        await task;
    }

    // ---- dispatch success ----

    [Fact]
    public async Task Worker_DispatchesPendingMessage()
    {
        var (worker, store, transport) = BuildWorker();
        var message = MakePendingMessage();
        await store.SaveAsync(message, TestContext.Current.CancellationToken);

        await RunWorkerUntilEmptyAsync(worker, store, TestContext.Current.CancellationToken);

        Assert.Contains(message.Id, transport.SentMessageIds);
    }

    [Fact]
    public async Task Worker_MarksMessageProcessedOnSuccess()
    {
        var (worker, store, _) = BuildWorker();
        var message = MakePendingMessage();
        await store.SaveAsync(message, TestContext.Current.CancellationToken);

        await RunWorkerUntilEmptyAsync(worker, store, TestContext.Current.CancellationToken);

        Assert.NotNull(store.Messages[0].ProcessedAt);
    }

    [Fact]
    public async Task Worker_DispatchesMultipleMessages()
    {
        var (worker, store, transport) = BuildWorker();
        for (var i = 0; i < 3; i++)
            await store.SaveAsync(MakePendingMessage(), TestContext.Current.CancellationToken);

        await RunWorkerUntilEmptyAsync(worker, store, TestContext.Current.CancellationToken);

        Assert.Equal(3, transport.SentMessageIds.Count);
        Assert.All(store.Messages, m => Assert.NotNull(m.ProcessedAt));
    }

    // ---- retry / dead-letter ----

    [Fact]
    public async Task Worker_IncrementsAttemptOnTransientFailure()
    {
        var (worker, store, transport) = BuildWorker(o => o.MaxAttempts = 5);
        transport.FailNextCount = 1;
        var message = MakePendingMessage(attemptCount: 0);
        await store.SaveAsync(message, TestContext.Current.CancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var task = worker.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && store.Messages[0].AttemptCount == 0)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        await task;

        Assert.Equal(1, store.Messages[0].AttemptCount);
        Assert.Null(store.Messages[0].FailedAt);
    }

    [Fact]
    public async Task Worker_DeadLettersAfterMaxAttempts()
    {
        const int maxAttempts = 3;
        var (worker, store, transport) = BuildWorker(o => o.MaxAttempts = maxAttempts);
        transport.FailAlways = true;
        var message = MakePendingMessage(attemptCount: maxAttempts - 1);
        await store.SaveAsync(message, TestContext.Current.CancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var task = worker.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && store.Messages[0].FailedAt is null)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        await task;

        Assert.NotNull(store.Messages[0].FailedAt);
        Assert.Null(store.Messages[0].ProcessedAt);
    }

    [Fact]
    public async Task Worker_SetsNextAttemptAtOnTransientFailure()
    {
        var (worker, store, transport) = BuildWorker(o => { o.MaxAttempts = 5; o.MaxRetryDelay = TimeSpan.FromMinutes(5); });
        transport.FailNextCount = 1;
        var message = MakePendingMessage(attemptCount: 0);
        await store.SaveAsync(message, TestContext.Current.CancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var task = worker.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && store.Messages[0].AttemptCount == 0)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        await task;

        // NextAttemptAt should be in the future (exponential backoff applied).
        Assert.NotNull(store.Messages[0].NextAttemptAt);
        Assert.True(store.Messages[0].NextAttemptAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Worker_RespectsConfiguredBatchSize()
    {
        const int batchSize = 2;
        const int messageCount = 5;

        var spy = new GetPendingSpy();
        var transport = new FakeTransport();

        for (var i = 0; i < messageCount; i++)
            await spy.SaveAsync(MakePendingMessage(), TestContext.Current.CancellationToken);

        var optionsValue = new OpinionatedEventingOptions();
        optionsValue.Outbox.PollInterval = TimeSpan.Zero;
        optionsValue.Outbox.BatchSize = batchSize;

        var services = new ServiceCollection();
        services.AddSingleton<IOutboxStore>(spy);
        services.AddSingleton<ITransport>(transport);
        var provider = services.BuildServiceProvider();

        var worker = new OutboxDispatcherWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(optionsValue),
            TimeProvider.System,
            NullLogger<OutboxDispatcherWorker>.Instance);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var task = worker.StartAsync(cts.Token);

        // Wait until every message has been processed — checked via Messages, not GetPendingAsync,
        // so the drain check itself does not pollute BatchSizesRequested.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !cts.Token.IsCancellationRequested)
        {
            if (spy.Messages.Count == messageCount && spy.Messages.All(m => m.ProcessedAt is not null))
                break;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        await task;

        // Every call to GetPendingAsync must have used the configured batch size cap.
        Assert.All(spy.BatchSizesRequested, size => Assert.Equal(batchSize, size));
        // All messages must eventually be dispatched across multiple cycles.
        Assert.Equal(messageCount, transport.SentMessageIds.Count);
    }

    // ---- fakes ----

    /// <summary>Wraps <see cref="InMemoryOutboxStore"/> and records every batchSize argument.</summary>
    private sealed class GetPendingSpy : IOutboxStore
    {
        private readonly InMemoryOutboxStore _inner = new();

        public List<int> BatchSizesRequested { get; } = [];

        // Expose the underlying messages list so RunWorkerUntilEmptyAsync can inspect it.
        public IReadOnlyList<OutboxMessage> Messages => _inner.Messages;

        public Task SaveAsync(OutboxMessage message, CancellationToken cancellationToken = default)
            => _inner.SaveAsync(message, cancellationToken);

        public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            BatchSizesRequested.Add(batchSize);
            return _inner.GetPendingAsync(batchSize, cancellationToken);
        }

        public Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default)
            => _inner.MarkProcessedAsync(id, cancellationToken);

        public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default)
            => _inner.MarkFailedAsync(id, error, cancellationToken);

        public Task IncrementAttemptAsync(Guid id, string error, DateTimeOffset? nextAttemptAt, CancellationToken cancellationToken = default)
            => _inner.IncrementAttemptAsync(id, error, nextAttemptAt, cancellationToken);

        public Task<int> DeleteProcessedAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
            => _inner.DeleteProcessedAsync(cutoff, cancellationToken);

        public Task<int> DeleteFailedAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
            => _inner.DeleteFailedAsync(cutoff, cancellationToken);
    }

    private sealed class FakeTransport : ITransport
    {
        public List<Guid> SentMessageIds { get; } = [];
        public int FailNextCount { get; set; }
        public bool FailAlways { get; set; }

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            if (FailAlways || FailNextCount > 0)
            {
                FailNextCount = Math.Max(0, FailNextCount - 1);
                throw new InvalidOperationException("Transport failure (test).");
            }

            SentMessageIds.Add(message.Id);
            return Task.CompletedTask;
        }
    }
}
