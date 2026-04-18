#nullable enable

using Xunit;

namespace OpinionatedEventing.AzureServiceBus.Tests.TestSupport;

/// <summary>
/// xUnit collection that serialises Azure Service Bus integration tests and shares a single emulator container.
/// DisableParallelization prevents message cross-contamination between tests sharing the same namespace topology.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AzureServiceBusCollection : ICollectionFixture<AzureServiceBusFixture>
{
    /// <summary>Collection name used on <see cref="CollectionAttribute"/>.</summary>
    public const string Name = "AzureServiceBus integration";
}
