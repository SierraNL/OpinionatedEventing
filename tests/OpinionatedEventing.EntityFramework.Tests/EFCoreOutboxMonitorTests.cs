using Microsoft.EntityFrameworkCore;
using OpinionatedEventing.EntityFramework.Tests.TestSupport;
using OpinionatedEventing.Outbox;
using Xunit;

namespace OpinionatedEventing.EntityFramework.Tests;

/// <summary>
/// Tests for <see cref="EFCoreOutboxMonitor{TDbContext}"/> covering pending and dead-letter counts.
/// Uses an in-process SQLite database.
/// </summary>
public sealed class EFCoreOutboxMonitorTests : IDisposable
{
    private readonly SqliteDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private EFCoreOutboxMonitor<SqliteTestDbContext> CreateMonitor(SqliteTestDbContext context)
        => new(context);

    private static OutboxMessage MakeMessage(DateTimeOffset? processedAt = null, DateTimeOffset? failedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        MessageType = "SomeType, SomeAssembly",
        Payload = "{}",
        MessageKind = "Event",
        CorrelationId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
        ProcessedAt = processedAt,
        FailedAt = failedAt,
    };

    [Fact]
    public async Task GetPendingCountAsync_returns_zero_when_outbox_is_empty()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        var monitor = CreateMonitor(context);

        int count = await monitor.GetPendingCountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetDeadLetterCountAsync_returns_zero_when_outbox_is_empty()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        var monitor = CreateMonitor(context);

        int count = await monitor.GetDeadLetterCountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetPendingCountAsync_counts_only_unprocessed_non_failed_messages()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        var monitor = CreateMonitor(context);

        context.Set<OutboxMessage>().AddRange(
            MakeMessage(),
            MakeMessage(processedAt: DateTimeOffset.UtcNow),
            MakeMessage(failedAt: DateTimeOffset.UtcNow));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        int count = await monitor.GetPendingCountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetDeadLetterCountAsync_counts_only_failed_messages()
    {
        await using SqliteTestDbContext context = _factory.CreateContext();
        var monitor = CreateMonitor(context);

        context.Set<OutboxMessage>().AddRange(
            MakeMessage(),
            MakeMessage(processedAt: DateTimeOffset.UtcNow),
            MakeMessage(failedAt: DateTimeOffset.UtcNow),
            MakeMessage(failedAt: DateTimeOffset.UtcNow));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        int count = await monitor.GetDeadLetterCountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, count);
    }
}
