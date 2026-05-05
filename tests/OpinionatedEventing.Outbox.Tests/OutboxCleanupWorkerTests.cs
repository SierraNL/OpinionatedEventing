#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Options;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Testing;
using Xunit;

namespace OpinionatedEventing.Outbox.Tests;

/// <summary>
/// Store that throws a configurable exception on every operation, used to exercise
/// the worker's error-handling catch block.
/// </summary>
file sealed class ThrowingOutboxStore(Exception exception) : IOutboxStore
{
    public Task SaveAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        => throw exception;
    public Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default)
        => throw exception;
    public Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default)
        => throw exception;
    public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken = default)
        => throw exception;
    public Task IncrementAttemptAsync(Guid id, string error, DateTimeOffset? nextAttemptAt, CancellationToken cancellationToken = default)
        => throw exception;
    public Task<int> DeleteProcessedAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        => throw exception;
    public Task<int> DeleteFailedAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        => throw exception;
}

public sealed class OutboxCleanupWorkerTests
{
    private static OutboxMessage MakeProcessedMessage(DateTimeOffset processedAt) => new()
    {
        Id = Guid.NewGuid(),
        MessageType = "Test.Event, Test",
        Payload = "{}",
        MessageKind = MessageKind.Event,
        CorrelationId = Guid.NewGuid(),
        CreatedAt = processedAt.AddMinutes(-1),
        ProcessedAt = processedAt,
    };

    private static OutboxMessage MakeFailedMessage(DateTimeOffset failedAt) => new()
    {
        Id = Guid.NewGuid(),
        MessageType = "Test.Event, Test",
        Payload = "{}",
        MessageKind = MessageKind.Event,
        CorrelationId = Guid.NewGuid(),
        CreatedAt = failedAt.AddMinutes(-1),
        FailedAt = failedAt,
        Error = "permanent error",
    };

    private static (OutboxCleanupWorker Worker, InMemoryOutboxStore Store)
        BuildWorker(Action<OutboxOptions>? configureOutbox = null)
    {
        var store = new InMemoryOutboxStore();

        var optionsValue = new OpinionatedEventingOptions();
        // Use a very short interval so the worker fires quickly in tests.
        optionsValue.Outbox.CleanupInterval = TimeSpan.FromMilliseconds(50);
        configureOutbox?.Invoke(optionsValue.Outbox);

        var services = new ServiceCollection();
        services.AddSingleton<IOutboxStore>(store);
        var provider = services.BuildServiceProvider();

        var worker = new OutboxCleanupWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(optionsValue),
            TimeProvider.System,
            NullLogger<OutboxCleanupWorker>.Instance);

        return (worker, store);
    }

    private static OutboxCleanupWorker BuildWorkerWithStore(IOutboxStore store, Action<OutboxOptions>? configureOutbox = null)
    {
        var optionsValue = new OpinionatedEventingOptions();
        optionsValue.Outbox.CleanupInterval = TimeSpan.FromMilliseconds(50);
        configureOutbox?.Invoke(optionsValue.Outbox);

        var services = new ServiceCollection();
        services.AddSingleton<IOutboxStore>(store);
        var provider = services.BuildServiceProvider();

        return new OutboxCleanupWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(optionsValue),
            TimeProvider.System,
            NullLogger<OutboxCleanupWorker>.Instance);
    }

    [Fact]
    public async Task Worker_DeletesProcessedMessagesOlderThanRetention()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var (worker, store) = BuildWorker(o => o.ProcessedRetention = TimeSpan.FromDays(7));

        var old = MakeProcessedMessage(now.AddDays(-8));
        var recent = MakeProcessedMessage(now.AddDays(-1));
        await store.SaveAsync(old, TestContext.Current.CancellationToken);
        await store.SaveAsync(recent, TestContext.Current.CancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var task = worker.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && store.Messages.Any(m => m.Id == old.Id))
            await Task.Delay(10, TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        await task;

        Assert.DoesNotContain(store.Messages, m => m.Id == old.Id);
        Assert.Contains(store.Messages, m => m.Id == recent.Id);
    }

    [Fact]
    public async Task Worker_SkipsProcessedDeletionWhenRetentionIsNull()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var (worker, store) = BuildWorker(o => o.ProcessedRetention = null);

        var old = MakeProcessedMessage(now.AddDays(-30));
        await store.SaveAsync(old, TestContext.Current.CancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var task = worker.StartAsync(cts.Token);

        // Let the worker run at least two cleanup cycles.
        await Task.Delay(200, TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        await task;

        Assert.Contains(store.Messages, m => m.Id == old.Id);
    }

    [Fact]
    public async Task Worker_DeletesFailedMessagesOlderThanRetention()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var (worker, store) = BuildWorker(o => o.FailedRetention = TimeSpan.FromDays(30));

        var old = MakeFailedMessage(now.AddDays(-31));
        var recent = MakeFailedMessage(now.AddDays(-1));
        await store.SaveAsync(old, TestContext.Current.CancellationToken);
        await store.SaveAsync(recent, TestContext.Current.CancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var task = worker.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && store.Messages.Any(m => m.Id == old.Id))
            await Task.Delay(10, TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        await task;

        Assert.DoesNotContain(store.Messages, m => m.Id == old.Id);
        Assert.Contains(store.Messages, m => m.Id == recent.Id);
    }

    [Fact]
    public async Task Worker_SkipsFailedDeletionWhenRetentionIsNull()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var (worker, store) = BuildWorker(o => o.FailedRetention = null);

        var old = MakeFailedMessage(now.AddDays(-365));
        await store.SaveAsync(old, TestContext.Current.CancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var task = worker.StartAsync(cts.Token);

        await Task.Delay(200, TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        await task;

        Assert.Contains(store.Messages, m => m.Id == old.Id);
    }

    [Fact]
    public async Task Worker_ContinuesRunningAfterStoreThrowsUnhandledException()
    {
        var throwingStore = new ThrowingOutboxStore(new InvalidOperationException("broker down"));
        var worker = BuildWorkerWithStore(throwingStore, o => o.ProcessedRetention = TimeSpan.FromDays(7));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var task = worker.StartAsync(cts.Token);

        // Give the worker enough time to fire at least one cleanup cycle.
        await Task.Delay(200, TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        // The worker must not have propagated the exception.
        await task;
    }
}
