#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Sagas.Diagnostics;
using OpinionatedEventing.Sagas.Options;

namespace OpinionatedEventing.Sagas;

/// <summary>
/// Hosted service that polls for expired saga instances and invokes their
/// <c>OnTimeout</c> handlers on the configured interval.
/// </summary>
public sealed class SagaTimeoutWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<SagaOptions> _options;
    private readonly ILogger<SagaTimeoutWorker> _logger;

    /// <summary>Initialises a new <see cref="SagaTimeoutWorker"/>.</summary>
    public SagaTimeoutWorker(
        IServiceProvider serviceProvider,
        TimeProvider timeProvider,
        IOptions<SagaOptions> options,
        ILogger<SagaTimeoutWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckTimeoutsAsync(stoppingToken);
            await Task.Delay(_options.Value.TimeoutCheckInterval, _timeProvider, stoppingToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task CheckTimeoutsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISagaStateStore>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
        var descriptors = _serviceProvider.GetServices<SagaDescriptor>();
        var serializerOptions = _options.Value.SerializerOptions;

        IReadOnlyList<SagaState> expired;
        try
        {
            expired = await store.GetExpiredAsync(_timeProvider.GetUtcNow(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query expired saga instances.");
            return;
        }

        foreach (var state in expired)
        {
            var descriptor = descriptors.FirstOrDefault(d => d.SagaTypeName == state.SagaType);
            if (descriptor is null)
            {
                _logger.LogWarning(
                    "No descriptor found for expired saga type '{SagaType}' (id={SagaId}).",
                    state.SagaType, state.Id);
                continue;
            }

            using var activity = SagaDiagnostics.StartSagaTimeoutActivity(state.SagaType, state.CorrelationId);
            try
            {
                await descriptor.HandleTimeoutAsync(
                    state, scope.ServiceProvider, store, publisher, _timeProvider, serializerOptions, ct);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex,
                    "Timeout handler failed for saga '{SagaType}' correlation='{CorrelationId}'.",
                    state.SagaType, state.CorrelationId);
            }
        }
    }
}
