#nullable enable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MSOptions = Microsoft.Extensions.Options.Options;
using OpinionatedEventing.RabbitMQ;
using Xunit;

namespace OpinionatedEventing.RabbitMQ.Tests;

public sealed class RabbitMqConnectionInitializerTests
{
    // ─── ResolveConnectionString ───────────────────────────────────────────────────

    [Fact]
    public void ResolveConnectionString_returns_explicit_ConnectionString()
    {
        var opts = new RabbitMQOptions { ConnectionString = "amqp://explicit/" };

        var result = RabbitMqConnectionInitializer.ResolveConnectionString(opts, config: null);

        Assert.Equal("amqp://explicit/", result);
    }

    [Fact]
    public void ResolveConnectionString_falls_back_to_Aspire_config()
    {
        var opts = new RabbitMQOptions();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:rabbitmq"] = "amqp://aspire/",
            })
            .Build();

        var result = RabbitMqConnectionInitializer.ResolveConnectionString(opts, config);

        Assert.Equal("amqp://aspire/", result);
    }

    [Fact]
    public void ResolveConnectionString_prefers_explicit_over_Aspire_config()
    {
        var opts = new RabbitMQOptions { ConnectionString = "amqp://explicit/" };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:rabbitmq"] = "amqp://aspire/",
            })
            .Build();

        var result = RabbitMqConnectionInitializer.ResolveConnectionString(opts, config);

        Assert.Equal("amqp://explicit/", result);
    }

    [Fact]
    public void ResolveConnectionString_uses_custom_AspireConnectionStringName()
    {
        var opts = new RabbitMQOptions { AspireConnectionStringName = "messaging" };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:messaging"] = "amqp://custom-aspire/",
            })
            .Build();

        var result = RabbitMqConnectionInitializer.ResolveConnectionString(opts, config);

        Assert.Equal("amqp://custom-aspire/", result);
    }

    [Fact]
    public void ResolveConnectionString_custom_name_ignores_default_key()
    {
        var opts = new RabbitMQOptions { AspireConnectionStringName = "messaging" };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:rabbitmq"] = "amqp://wrong/",
            })
            .Build();

        Assert.Throws<InvalidOperationException>(
            () => RabbitMqConnectionInitializer.ResolveConnectionString(opts, config));
    }

    [Fact]
    public void ResolveConnectionString_throws_when_no_connection_string_available()
    {
        var opts = new RabbitMQOptions();

        Assert.Throws<InvalidOperationException>(
            () => RabbitMqConnectionInitializer.ResolveConnectionString(opts, config: null));
    }

    // ─── StopAsync ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StopAsync_completes_without_exception()
    {
        var opts = MSOptions.Create(new RabbitMQOptions { ConnectionString = "amqp://localhost/" });
        var holder = new RabbitMqConnectionHolder();

        await using var initializer = new RabbitMqConnectionInitializer(
            holder,
            opts,
            NullLogger<RabbitMqConnectionInitializer>.Instance);

        // StopAsync is a no-op — just verify it doesn't throw
        await initializer.StopAsync(TestContext.Current.CancellationToken);
    }

    // ─── StartAsync failure ────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_faults_holder_and_rethrows_on_connection_failure()
    {
        // A connection string that points to a host that will refuse immediately
        // on localhost with an unlikely port so the test doesn't hang.
        var opts = MSOptions.Create(new RabbitMQOptions { ConnectionString = "amqp://127.0.0.1:1/" });
        var holder = new RabbitMqConnectionHolder();

        await using var initializer = new RabbitMqConnectionInitializer(
            holder,
            opts,
            NullLogger<RabbitMqConnectionInitializer>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<Exception>(() => initializer.StartAsync(cts.Token));

        // Holder must be faulted so that consumers surface the error instead of hanging.
        await Assert.ThrowsAnyAsync<Exception>(
            () => holder.GetConnectionAsync(CancellationToken.None));
    }
}
