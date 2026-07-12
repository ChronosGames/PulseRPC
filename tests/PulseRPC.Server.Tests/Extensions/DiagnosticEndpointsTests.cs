using System.Text.Json;
using FluentAssertions;
using PulseRPC.Server.Extensions;
using Xunit;

namespace PulseRPC.Server.Tests.Extensions;

public sealed class DiagnosticEndpointsTests
{
    [Fact]
    public void QueueStats_WithoutCapacityProvider_MustNotFabricateSaturation()
    {
        var endpoints = new DiagnosticEndpoints(new PipelineMetricsCollector());

        using var document = JsonDocument.Parse(endpoints.GetQueueStats());
        var l1Queue = document.RootElement.GetProperty("l1_queue");

        l1Queue.GetProperty("capacity").ValueKind.Should().Be(JsonValueKind.Null);
        l1Queue.GetProperty("saturation").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
