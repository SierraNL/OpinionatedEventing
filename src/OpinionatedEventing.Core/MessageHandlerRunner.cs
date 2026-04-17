#nullable enable

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpinionatedEventing.Options;

namespace OpinionatedEventing;

/// <summary>
/// Default implementation of <see cref="IMessageHandlerRunner"/>.
/// Registered as a singleton; creates a new DI scope per message dispatch.
/// </summary>
public sealed class MessageHandlerRunner : IMessageHandlerRunner
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OpinionatedEventingOptions> _options;
    private readonly ILogger<MessageHandlerRunner> _logger;

    /// <summary>Initialises a new <see cref="MessageHandlerRunner"/>.</summary>
    public MessageHandlerRunner(
        IServiceScopeFactory scopeFactory,
        IOptions<OpinionatedEventingOptions> options,
        ILogger<MessageHandlerRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task RunAsync(
        string messageType,
        string messageKind,
        string payload,
        Guid correlationId,
        Guid? causationId,
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var messagingContext = scope.ServiceProvider.GetRequiredService<MessagingContext>();
        messagingContext.Initialize(correlationId, causationId);

        var type = Type.GetType(messageType)
            ?? throw new InvalidOperationException($"Cannot resolve type '{messageType}'.");

        var message = JsonSerializer.Deserialize(payload, type, _options.Value.SerializerOptions)
            ?? throw new InvalidOperationException($"Deserialised null for type '{messageType}'.");

        switch (messageKind)
        {
            case "Event":
                await DispatchEventAsync(scope.ServiceProvider, type, message, ct).ConfigureAwait(false);
                break;
            case "Command":
                await DispatchCommandAsync(scope.ServiceProvider, type, message, ct).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException($"Unknown message kind '{messageKind}'.");
        }
    }

    private async Task DispatchEventAsync(
        IServiceProvider sp,
        Type eventType,
        object message,
        CancellationToken ct)
    {
        var handlerType = typeof(IEventHandler<>).MakeGenericType(eventType);
        var handlers = sp.GetServices(handlerType).ToList();

        if (handlers.Count == 0)
        {
            _logger.LogWarning("No handlers registered for event type '{EventType}'.", eventType.FullName);
            return;
        }

        // HandleAsync is defined on IEventHandler<T> — GetMethod never returns null here.
        var handleMethod = handlerType.GetMethod("HandleAsync")!;

        foreach (var handler in handlers)
        {
            // Invoke returns Task (non-void), never null.
            await ((Task)handleMethod.Invoke(handler, [message, ct])!).ConfigureAwait(false);
        }
    }

    private static async Task DispatchCommandAsync(
        IServiceProvider sp,
        Type commandType,
        object message,
        CancellationToken ct)
    {
        var handlerType = typeof(ICommandHandler<>).MakeGenericType(commandType);
        var handler = sp.GetRequiredService(handlerType);

        // HandleAsync is defined on ICommandHandler<T> — GetMethod never returns null here.
        var handleMethod = handlerType.GetMethod("HandleAsync")!;
        // Invoke returns Task (non-void), never null.
        await ((Task)handleMethod.Invoke(handler, [message, ct])!).ConfigureAwait(false);
    }
}
