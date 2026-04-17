using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpinionatedEventing.Sagas;
using OpinionatedEventing.Testing;

namespace OpinionatedEventing.Sagas.Tests.TestSupport;

/// <summary>
/// Builds a scoped DI container for saga unit tests, wiring up
/// <see cref="InMemorySagaStateStore"/>, <see cref="FakePublisher"/>, and
/// <see cref="ISagaDispatcher"/> without any infrastructure dependencies.
/// </summary>
internal sealed class SagaTestHarness : IAsyncDisposable
{
    private readonly ServiceProvider _root;
    private readonly IServiceScope _scope;

    public ISagaDispatcher Dispatcher { get; }
    public InMemorySagaStateStore Store { get; }
    public FakePublisher Publisher { get; }
    public IServiceScope Scope => _scope;

    private SagaTestHarness(
        ServiceProvider root,
        IServiceScope scope,
        ISagaDispatcher dispatcher,
        InMemorySagaStateStore store,
        FakePublisher publisher)
    {
        _root = root;
        _scope = scope;
        Dispatcher = dispatcher;
        Store = store;
        Publisher = publisher;
    }

    public static SagaTestHarness Create(Action<IServiceCollection> configure, TimeProvider? timeProvider = null)
    {
        var store = new InMemorySagaStateStore();
        var publisher = new FakePublisher();

        var services = new ServiceCollection();
        services.AddSingleton<ISagaStateStore>(store);
        services.AddSingleton<IPublisher>(publisher);
        services.AddSingleton(timeProvider ?? TimeProvider.System);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<SagaTimeoutWorker>),
            NullLogger<SagaTimeoutWorker>.Instance);
        services.AddOpinionatedEventingSagas();
        configure(services);

        var root = services.BuildServiceProvider(validateScopes: true);
        var scope = root.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ISagaDispatcher>();

        return new SagaTestHarness(root, scope, dispatcher, store, publisher);
    }

    public ValueTask DisposeAsync()
    {
        _scope.Dispose();
        return _root.DisposeAsync();
    }
}
