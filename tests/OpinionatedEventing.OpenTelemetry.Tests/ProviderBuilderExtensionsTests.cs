#nullable enable

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpinionatedEventing.Diagnostics;
using OpinionatedEventing.OpenTelemetry;
using Xunit;

namespace OpinionatedEventing.OpenTelemetry.Tests;

public sealed class ProviderBuilderExtensionsTests
{
    [Fact]
    public void AddOpinionatedEventingInstrumentation_ReturnsSameBuilderForChaining()
    {
        var builder = Sdk.CreateTracerProviderBuilder();
        var result = builder.AddOpinionatedEventingInstrumentation();
        Assert.Same(builder, result);
    }

    [Fact]
    public void AddOpinionatedEventingMetrics_ReturnsSameBuilderForChaining()
    {
        var builder = Sdk.CreateMeterProviderBuilder();
        var result = builder.AddOpinionatedEventingMetrics();
        Assert.Same(builder, result);
    }

    [Fact]
    public void AddOpinionatedEventingInstrumentation_BuildSucceeds()
    {
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddOpinionatedEventingInstrumentation()
            .Build();

        Assert.NotNull(provider);
    }

    [Fact]
    public void AddOpinionatedEventingMetrics_BuildSucceeds()
    {
        using var provider = Sdk.CreateMeterProviderBuilder()
            .AddOpinionatedEventingMetrics()
            .Build();

        Assert.NotNull(provider);
    }

    [Fact]
    public void ActivitySourceName_MatchesExpectedConstant()
    {
        Assert.Equal("OpinionatedEventing", EventingTelemetry.ActivitySourceName);
    }

    [Fact]
    public void MeterName_MatchesExpectedConstant()
    {
        Assert.Equal("OpinionatedEventing", EventingTelemetry.MeterName);
    }
}
