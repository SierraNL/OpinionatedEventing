#nullable enable

using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace OpinionatedEventing;

/// <summary>
/// Caches compiled <see cref="Func{T1,T2,T3,TResult}"/> delegates for event and command handler dispatch.
/// Each delegate is built once via expression trees and stored by message type, eliminating per-dispatch
/// reflection (<c>MakeGenericType</c>, <c>GetMethod</c>, <c>MethodInfo.Invoke</c>).
/// </summary>
internal static class HandlerDispatcherCache
{
    // Static caches are intentional: each entry holds an immutable compiled delegate and a Type reference.
    // Computation is idempotent — building the same entry twice always yields an equivalent result —
    // so concurrent first-miss races are harmless and no locking beyond ConcurrentDictionary is needed.
    private static readonly ConcurrentDictionary<Type, Entry> s_eventEntries = new();
    private static readonly ConcurrentDictionary<Type, Entry> s_commandEntries = new();

    /// <summary>
    /// Returns (or builds and caches) the dispatch entry for an event of <paramref name="messageType"/>.
    /// </summary>
    internal static Entry GetEventEntry(Type messageType)
        => s_eventEntries.GetOrAdd(messageType, static t => Build(typeof(IEventHandler<>), t));

    /// <summary>
    /// Returns (or builds and caches) the dispatch entry for a command of <paramref name="messageType"/>.
    /// </summary>
    internal static Entry GetCommandEntry(Type messageType)
        => s_commandEntries.GetOrAdd(messageType, static t => Build(typeof(ICommandHandler<>), t));

    private static Entry Build(Type openHandlerInterface, Type messageType)
    {
        Type handlerServiceType = openHandlerInterface.MakeGenericType(messageType);

        ParameterExpression handlerParam = Expression.Parameter(typeof(object), "handler");
        ParameterExpression messageParam = Expression.Parameter(typeof(object), "message");
        ParameterExpression ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        MethodCallExpression call = Expression.Call(
            Expression.Convert(handlerParam, handlerServiceType),
            handlerServiceType.GetMethod("HandleAsync")!, // HandleAsync is the sole method on both handler interfaces

            Expression.Convert(messageParam, messageType),
            ctParam);

        Func<object, object, CancellationToken, Task> dispatcher =
            Expression.Lambda<Func<object, object, CancellationToken, Task>>(
                call, handlerParam, messageParam, ctParam)
            .Compile();

        return new Entry(handlerServiceType, dispatcher);
    }

    /// <summary>Cached data for a single message type's dispatch path.</summary>
    internal readonly record struct Entry(
        Type HandlerServiceType,
        Func<object, object, CancellationToken, Task> Dispatcher);
}
