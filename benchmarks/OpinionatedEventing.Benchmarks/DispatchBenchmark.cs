#nullable enable

using System.Reflection;
using System.Runtime.ExceptionServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpinionatedEventing.Benchmarks;

/// <summary>
/// Compares the per-dispatch cost of the old reflection path against the new
/// expression-tree compiled delegate cache introduced in issue #109.
///
/// Run with: dotnet run -c Release --project benchmarks/OpinionatedEventing.Benchmarks
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class DispatchBenchmark
{
    private sealed record BenchEvent(int Value) : IEvent;

    private sealed class NoopEventHandler : IEventHandler<BenchEvent>
    {
        public Task HandleAsync(BenchEvent @event, CancellationToken ct) => Task.CompletedTask;
    }

    private object _handler = null!;
    private object _message = null!;
    private Type _eventType = null!;
    private Func<object, object, CancellationToken, Task> _compiledDispatcher = null!;
    private IMessageHandlerRunner _runner = null!;
    private ServiceProvider _provider = null!;

    [GlobalSetup]
    public void Setup()
    {
        _handler = new NoopEventHandler();
        _message = new BenchEvent(42);
        _eventType = typeof(BenchEvent);

        // ---- compiled cache (new path) ----
        _compiledDispatcher = HandlerDispatcherCache.GetEventEntry(_eventType).Dispatcher;

        // ---- full runner (end-to-end) ----
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddOpinionatedEventing();
        services.AddScoped<IEventHandler<BenchEvent>, NoopEventHandler>();
        _provider = services.BuildServiceProvider();
        _runner = _provider.GetRequiredService<IMessageHandlerRunner>();
    }

    [GlobalCleanup]
    public async ValueTask Cleanup() => await _provider.DisposeAsync();

    /// <summary>Baseline: the old reflection path (MakeGenericType + GetMethod + Invoke per call).</summary>
    [Benchmark(Baseline = true)]
    public async Task Reflection()
    {
        // Reproduce the original hot-path exactly.
        Type handlerType = typeof(IEventHandler<>).MakeGenericType(_eventType);
        MethodInfo method = handlerType.GetMethod("HandleAsync")!;
        Task task;
        try
        {
            task = (Task)method.Invoke(_handler, [_message, CancellationToken.None])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        await task.ConfigureAwait(false);
    }

    /// <summary>New path: expression-tree compiled delegate, looked up from ConcurrentDictionary.</summary>
    [Benchmark]
    public Task CompiledDelegate()
        => _compiledDispatcher(_handler, _message, CancellationToken.None);

    /// <summary>Full end-to-end runner dispatch including DI scope creation and JSON deserialization.</summary>
    [Benchmark]
    public Task FullRunner()
        => _runner.RunAsync(
            typeof(BenchEvent).AssemblyQualifiedName!,
            "Event",
            "{\"Value\":42}",
            Guid.NewGuid(),
            null,
            CancellationToken.None);
}
