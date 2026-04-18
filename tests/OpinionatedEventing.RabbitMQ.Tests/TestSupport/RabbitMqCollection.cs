#nullable enable

using Xunit;

namespace OpinionatedEventing.RabbitMQ.Tests.TestSupport;

/// <summary>
/// xUnit collection that serialises RabbitMQ integration tests and shares a single container.
/// DisableParallelization prevents message cross-contamination between tests sharing the same topology.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqFixture>
{
    /// <summary>Collection name used on <see cref="CollectionAttribute"/>.</summary>
    public const string Name = "RabbitMQ integration";
}
