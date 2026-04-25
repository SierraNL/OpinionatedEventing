using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpinionatedEventing.EntityFramework.Tests.TestSupport;
using OpinionatedEventing.Outbox;
using OpinionatedEventing.Sagas;
using Xunit;

namespace OpinionatedEventing.EntityFramework.Tests;

/// <summary>
/// Unit tests verifying that a UTC-ticks <see cref="ValueConverter{TModel,TProvider}"/> is
/// applied to all <see cref="DateTimeOffset"/> properties on <see cref="OutboxMessage"/> and
/// <see cref="SagaState"/> when the SQLite provider is active, and that no converter is applied
/// for non-SQLite providers.
/// </summary>
public sealed class SqliteDateTimeOffsetConverterTests : IDisposable
{
    private readonly SqliteDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData(nameof(OutboxMessage.CreatedAt))]
    [InlineData(nameof(OutboxMessage.ProcessedAt))]
    [InlineData(nameof(OutboxMessage.FailedAt))]
    public void OutboxMessage_DateTimeOffset_properties_use_long_converter_on_SQLite(string propertyName)
    {
        using var ctx = _factory.CreateContext();
        var property = ctx.Model.FindEntityType(typeof(OutboxMessage))!.FindProperty(propertyName)!;

        var converter = property.GetValueConverter();

        Assert.NotNull(converter);
        Assert.Equal(typeof(long), converter.ProviderClrType);
    }

    [Theory]
    [InlineData(nameof(SagaState.CreatedAt))]
    [InlineData(nameof(SagaState.UpdatedAt))]
    [InlineData(nameof(SagaState.ExpiresAt))]
    public void SagaState_DateTimeOffset_properties_use_long_converter_on_SQLite(string propertyName)
    {
        using var ctx = _factory.CreateContext();
        var property = ctx.Model.FindEntityType(typeof(SagaState))!.FindProperty(propertyName)!;

        var converter = property.GetValueConverter();

        Assert.NotNull(converter);
        Assert.Equal(typeof(long), converter.ProviderClrType);
    }

    [Theory]
    [InlineData(nameof(OutboxMessage.CreatedAt))]
    [InlineData(nameof(OutboxMessage.ProcessedAt))]
    [InlineData(nameof(OutboxMessage.FailedAt))]
    public void OutboxMessage_DateTimeOffset_properties_have_no_converter_for_non_SQLite(string propertyName)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var ctx = new TestDbContext(options);
        var property = ctx.Model.FindEntityType(typeof(OutboxMessage))!.FindProperty(propertyName)!;

        Assert.Null(property.GetValueConverter());
    }

    [Theory]
    [InlineData(nameof(SagaState.CreatedAt))]
    [InlineData(nameof(SagaState.UpdatedAt))]
    [InlineData(nameof(SagaState.ExpiresAt))]
    public void SagaState_DateTimeOffset_properties_have_no_converter_for_non_SQLite(string propertyName)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var ctx = new TestDbContext(options);
        var property = ctx.Model.FindEntityType(typeof(SagaState))!.FindProperty(propertyName)!;

        Assert.Null(property.GetValueConverter());
    }
}
