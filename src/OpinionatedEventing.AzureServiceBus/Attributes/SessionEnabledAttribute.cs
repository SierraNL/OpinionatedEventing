#nullable enable

namespace OpinionatedEventing.AzureServiceBus.Attributes;

/// <summary>
/// Marks a command type as requiring a session-enabled queue so that messages sharing the
/// same session ID are processed in order.
/// The session ID defaults to <see cref="OpinionatedEventing.IMessagingContext.CorrelationId"/>.
/// </summary>
/// <remarks>
/// Requires <see cref="OpinionatedEventing.AzureServiceBus.AzureServiceBusOptions.EnableSessions"/>
/// to be set to <see langword="true"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SessionEnabledAttribute : Attribute { }
